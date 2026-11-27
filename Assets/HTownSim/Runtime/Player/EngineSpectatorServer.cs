#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HTownSim.Runtime.Incidents.Sources;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Streams every fleet unit's server-owned pose to one local spectator, and takes its test-incident command. The stream is a
    /// version line (<see cref="Header"/>) and then, ten times a second, tab-separated lines: <c>U</c>, unit name, x, y, z, yaw and
    /// light flags (1 lights, 2 siren) for each unit; <c>F</c>, incident id, x, y, z, heat, growth and 1 when out for each fire; <c>C</c> and an incident id when a fire is
    /// gone. <c>M</c> and a message answers a command. The spectator sends <c>TEST</c> to start a test structure fire.
    /// </summary>
    internal sealed class EngineSpectatorServer : MonoBehaviour
    {
        internal const int Port = 7778;

        /// <summary>The first line of the stream; a spectator that does not know it stops.</summary>
        internal const string Header = "HTOWNSIM CITY 2";

        /// <summary>The first line of the older stream, which carried Engine 07 alone as four numbers a line. Spectators still read it.</summary>
        internal const string LegacyHeader = "HTOWNSIM ENGINE07 1";

        private const int SendMilliseconds = 100;

        private TcpListener? _listener;
        private TcpClient? _client;
        private CancellationTokenSource? _shutdown;

        internal async void Begin(AiFleet fleet, LiveIncidentFeed feed, LiveIncidentFires? fires)
        {
            var listener = new TcpListener(CityReadinessListener.BindAddress, Port);
            try
            {
                listener.Start(1);
            }
            catch (SocketException exception)
            {
                listener.Stop();
                Debug.LogError("Engine spectator server could not open port " + Port + ": " + exception.Message);
                return;
            }

            _listener = listener;
            _shutdown = new CancellationTokenSource();
            var shutdown = _shutdown.Token;

            while (!shutdown.IsCancellationRequested && fleet != null)
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    if (shutdown.IsCancellationRequested)
                    {
                        return;
                    }

                    _client = client;
                    client.SendTimeout = 1000;
                    client.NoDelay = true;
                    var stream = client.GetStream();
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false };
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    await writer.WriteLineAsync(Header);
                    var commands = ReadCommands(reader, writer, feed, shutdown);
                    var sentFires = new HashSet<string>();

                    while (!shutdown.IsCancellationRequested && fleet != null && !commands.IsCompleted)
                    {
                        foreach (var unit in fleet.Units)
                        {
                            if (unit == null)
                            {
                                continue;
                            }

                            var position = unit.transform.position;
                            var yaw = unit.transform.eulerAngles.y;
                            await writer.WriteLineAsync(FormattableString.Invariant($"U\t{unit.UnitName}\t{position.x:R}\t{position.y:R}\t{position.z:R}\t{yaw:R}\t{unit.LightFlags}"));
                        }

                        var burning = new HashSet<string>();
                        if (fires != null)
                        {
                            foreach (var fire in fires.Snapshot())
                            {
                                burning.Add(fire.Id);
                                await writer.WriteLineAsync(FormattableString.Invariant(
                                    $"F\t{fire.Id}\t{fire.Position.x:R}\t{fire.Position.y:R}\t{fire.Position.z:R}\t{fire.Heat:R}\t{fire.Growth:R}\t{(fire.Out ? 1 : 0)}"));
                            }
                        }

                        // A fire that left the list is gone from the city: the spectator removes it.
                        foreach (var gone in new List<string>(sentFires))
                        {
                            if (!burning.Contains(gone))
                            {
                                await writer.WriteLineAsync("C\t" + gone);
                                sentFires.Remove(gone);
                            }
                        }

                        sentFires.UnionWith(burning);
                        await writer.FlushAsync();
                        await Task.Delay(SendMilliseconds, shutdown);
                    }
                }
                catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
                {
                    if (shutdown.IsCancellationRequested)
                    {
                        return;
                    }
                }
                finally
                {
                    _client = null;
                }
            }
        }

        /// <summary>Reads the spectator's commands until it disconnects. Runs on the main thread (Unity's context), so it can drive the fleet.</summary>
        private static async Task ReadCommands(StreamReader reader, StreamWriter writer, LiveIncidentFeed feed, CancellationToken shutdown)
        {
            try
            {
                while (!shutdown.IsCancellationRequested && await reader.ReadLineAsync() is { } line)
                {
                    if (line != "TEST")
                    {
                        continue;
                    }

                    var sent = feed.SubmitTestFire();
                    var message = sent is { } result
                        ? FormattableString.Invariant($"Structure fire at {result.World.x:0}, {result.World.z:0}: {string.Join(", ", result.Units)} responding")
                        : "No unit could be sent to the test fire (all busy, or no street reaches it)";
                    await writer.WriteLineAsync("M\t" + message);
                    await writer.FlushAsync();
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // The spectator went away; the stream loop sees the task complete and waits for the next one.
            }
        }

        private void OnDisable()
        {
            _shutdown?.Cancel();
            _listener?.Stop();
            _client?.Dispose();
            _shutdown?.Dispose();
            _shutdown = null;
            _listener = null;
        }
    }
}
