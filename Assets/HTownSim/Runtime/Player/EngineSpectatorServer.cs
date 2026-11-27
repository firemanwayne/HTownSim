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
    /// The city server's connection point. Any number of clients connect at once; each is streamed the city (every unit its role may see,
    /// and the fires) ten times a second as tab-separated lines: <c>U</c>, unit name, x, y, z, yaw and light flags (1 lights, 2 siren,
    /// 4 horn) for each unit; <c>F</c>, incident id, x, y, z, heat, growth and 1 when out for each fire; <c>C</c> and an incident id when
    /// a fire is gone. <c>M</c> and a message answers a command; <c>S</c> and a unit name says the connection now drives that unit (an
    /// empty name: it no longer does). A client may send <c>TEST</c> (start a test structure fire), <c>ROLE name</c> (see less),
    /// <c>SEAT</c> and a unit name (drive that unit), <c>LEAVE</c> (stop driving it) and, while seated, <c>P</c>, x, y, z, yaw and light
    /// flags: where its vehicle is. The player's client moves its own vehicle; the server owns everything else (the other units, the
    /// incidents, the fires, who may see what) and mirrors the seated unit from those reports.
    /// </summary>
    internal sealed class EngineSpectatorServer : MonoBehaviour
    {
        internal const int Port = 7778;

        /// <summary>The first line of the stream; a client that does not know it stops.</summary>
        internal const string Header = "HTOWNSIM CITY 3";

        /// <summary>The first line of the stream before seats. Clients still read it; it has no seat commands.</summary>
        internal const string PreviousHeader = "HTOWNSIM CITY 2";

        /// <summary>The first line of the older stream, which carried Engine 07 alone as four numbers a line. Spectators still read it.</summary>
        internal const string LegacyHeader = "HTOWNSIM ENGINE07 1";

        private const int SendMilliseconds = 100;

        /// <summary>The command that asks to see the city as a role (<c>ROLE Fire</c>), which may only narrow what the connection sees.</summary>
        internal const string RoleCommand = "ROLE ";

        /// <summary>The command that takes a unit's seat (<c>SEAT</c>, a tab, then the unit name).</summary>
        internal const string SeatCommand = "SEAT\t";

        internal const string LeaveCommand = "LEAVE";

        /// <summary>A seated client's pose report: <c>P</c>, x, y, z, yaw, light flags (tab separated).</summary>
        internal const string PoseCommand = "P\t";

        /// <summary>The server's line that sends a seated unit to a call: <c>D</c>, a tab, then the incident record as JSON.</summary>
        internal const string DispatchLine = "D\t";

        /// <summary>A seated client's report that it finished its call: <c>DONE</c>, incident id, 1 when it took a patient to a hospital (tab separated).</summary>
        internal const string DoneCommand = "DONE\t";

        /// <summary>The largest coordinate a pose report may carry (a guard against garbage numbers, not a rule of play).</summary>
        private const float PoseLimit = 100000f;

        /// <summary>One connection: what it is granted, what it asked to see, the unit it drives, and a lock so its two loops never write at once.</summary>
        private sealed class Session
        {
            internal Session(StreamWriter writer)
            {
                Writer = writer;
            }

            private StreamWriter Writer { get; }

            private SemaphoreSlim WriteLock { get; } = new SemaphoreSlim(1, 1);

            /// <summary>What the connection may see at most: a spectator sees everything; a seated player sees what their unit's service sees.</summary>
            internal ViewerRole Granted { get; set; } = ViewerRole.Spectator;

            internal volatile ViewerRole Role = ViewerRole.Spectator;

            /// <summary>The unit this connection drives, or null.</summary>
            internal AiUnit? Seat { get; set; }

            internal async Task SendAsync(string line, bool flush)
            {
                await WriteLock.WaitAsync();
                try
                {
                    await Writer.WriteLineAsync(line);
                    if (flush)
                    {
                        await Writer.FlushAsync();
                    }
                }
                finally
                {
                    WriteLock.Release();
                }
            }

            internal async Task FlushAsync()
            {
                await WriteLock.WaitAsync();
                try
                {
                    await Writer.FlushAsync();
                }
                finally
                {
                    WriteLock.Release();
                }
            }
        }

        private TcpListener? _listener;
        private CancellationTokenSource? _shutdown;
        private readonly List<TcpClient> _clients = new List<TcpClient>();

        internal async void Begin(AiFleet fleet, LiveIncidentFeed feed, LiveIncidentFires? fires)
        {
            var listener = new TcpListener(CityReadinessListener.BindAddress, Port);
            try
            {
                listener.Start();
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
                    var client = await listener.AcceptTcpClientAsync();
                    if (shutdown.IsCancellationRequested)
                    {
                        client.Dispose();
                        return;
                    }

                    // Each connection runs on its own; the loop goes straight back to waiting for the next.
                    _ = Serve(client, fleet, feed, fires, shutdown);
                }
                catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
                {
                    if (shutdown.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>Streams the city to one connection and reads its commands until it goes away.</summary>
        private async Task Serve(TcpClient client, AiFleet fleet, LiveIncidentFeed feed, LiveIncidentFires? fires, CancellationToken shutdown)
        {
            Session? session = null;
            _clients.Add(client);
            try
            {
                client.SendTimeout = 1000;
                client.NoDelay = true;
                var stream = client.GetStream();
                using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false };
                using var reader = new StreamReader(stream, Encoding.UTF8);
                session = new Session(writer);
                await session.SendAsync(Header, true);
                var commands = ReadCommands(reader, session, fleet, feed, shutdown);
                var sentFires = new HashSet<string>();

                while (!shutdown.IsCancellationRequested && fleet != null && !commands.IsCompleted)
                {
                    var role = session.Role;
                    var own = session.Seat;

                    // The call a seated unit was sent to goes to its player once: D and the incident record as JSON.
                    if (own != null && own.SeatIncident.Length > 0 && !own.SeatNotified)
                    {
                        own.SeatNotified = true;
                        await session.SendAsync(DispatchLine + own.SeatRecordJson, false);
                    }

                    foreach (var unit in fleet.Units)
                    {
                        // A location the connection's role may not see is never sent; a player is not sent their own unit back.
                        if (unit == null || unit == own || !role.CanSeeUnit(unit.UnitName))
                        {
                            continue;
                        }

                        var position = unit.transform.position;
                        var yaw = unit.transform.eulerAngles.y;
                        await session.SendAsync(FormattableString.Invariant($"U\t{unit.UnitName}\t{position.x:R}\t{position.y:R}\t{position.z:R}\t{yaw:R}\t{unit.LightFlags}"), false);
                    }

                    var burning = new HashSet<string>();
                    if (fires != null)
                    {
                        foreach (var fire in fires.Snapshot())
                        {
                            burning.Add(fire.Id);
                            await session.SendAsync(FormattableString.Invariant(
                                $"F\t{fire.Id}\t{fire.Position.x:R}\t{fire.Position.y:R}\t{fire.Position.z:R}\t{fire.Heat:R}\t{fire.Growth:R}\t{(fire.Out ? 1 : 0)}"), false);
                        }
                    }

                    // A fire that left the list is gone from the city: the client removes it.
                    foreach (var gone in new List<string>(sentFires))
                    {
                        if (!burning.Contains(gone))
                        {
                            await session.SendAsync("C\t" + gone, false);
                            sentFires.Remove(gone);
                        }
                    }

                    sentFires.UnionWith(burning);
                    await session.FlushAsync();
                    await Task.Delay(SendMilliseconds, shutdown);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
            {
                // The connection went away (or the server is stopping); the cleanup below frees its seat.
            }
            finally
            {
                // A player who leaves, by command or by dropping, gives the unit back to the computer.
                session?.Seat?.Unseat();
                _clients.Remove(client);
                client.Dispose();
            }
        }

        /// <summary>Reads one connection's commands until it disconnects. Runs on the main thread (Unity's context), so it can drive the fleet.</summary>
        private static async Task ReadCommands(StreamReader reader, Session session, AiFleet fleet, LiveIncidentFeed feed, CancellationToken shutdown)
        {
            try
            {
                while (!shutdown.IsCancellationRequested && await reader.ReadLineAsync() is { } line)
                {
                    if (line.StartsWith(PoseCommand, StringComparison.Ordinal))
                    {
                        ApplyPose(session, line);

                        continue;
                    }

                    if (line.StartsWith(RoleCommand, StringComparison.Ordinal))
                    {
                        // A connection may ask to see less than it is granted (a spectator may view as fire), never more.
                        var requested = ViewerRole.Parse(line.Substring(RoleCommand.Length));
                        if (requested != null && requested.IsWithin(session.Granted))
                        {
                            session.Role = requested;
                            await session.SendAsync("M\tViewing as " + requested.Name, true);
                        }
                        else
                        {
                            await session.SendAsync("M\tThat view is not allowed for this connection", true);
                        }

                        continue;
                    }

                    if (line.StartsWith(SeatCommand, StringComparison.Ordinal))
                    {
                        await TakeSeat(session, fleet, line.Substring(SeatCommand.Length));

                        continue;
                    }

                    if (line.StartsWith(DoneCommand, StringComparison.Ordinal))
                    {
                        var parts = line.Split('\t');
                        if (parts.Length == 3)
                        {
                            session.Seat?.CompleteSeatCall(parts[1], parts[2] == "1");
                        }

                        continue;
                    }

                    if (line == LeaveCommand)
                    {
                        await LeaveSeat(session);

                        continue;
                    }

                    if (line != "TEST")
                    {
                        continue;
                    }

                    var sent = feed.SubmitTestFire();
                    var message = sent is { } result
                        ? FormattableString.Invariant($"Structure fire at {result.World.x:0}, {result.World.z:0}: {string.Join(", ", result.Units)} responding")
                        : "No unit could be sent to the test fire (all busy, or no street reaches it)";
                    await session.SendAsync("M\t" + message, true);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // The client went away; the stream loop sees the task complete and ends the connection.
            }
        }

        /// <summary>Seats the connection in a unit: refused when it already drives one, the unit is unknown, taken or out on a call.</summary>
        private static async Task TakeSeat(Session session, AiFleet fleet, string unitName)
        {
            if (session.Seat != null)
            {
                await session.SendAsync("M\tYou already drive " + session.Seat.UnitName, true);

                return;
            }

            var unit = fleet.Find(unitName.Trim());
            if (unit == null)
            {
                await session.SendAsync("M\tNo such unit: " + unitName.Trim(), true);

                return;
            }

            if (!unit.Seat())
            {
                await session.SendAsync("M\t" + unit.UnitName + " is taken or out on a call", true);

                return;
            }

            // The seat sets what the connection may see: its unit's service, never more (the server assigns the role, not the client).
            session.Seat = unit;
            session.Granted = ViewerRole.ForUnit(unit.UnitName);
            session.Role = session.Granted;
            await session.SendAsync("S\t" + unit.UnitName, true);
        }

        private static async Task LeaveSeat(Session session)
        {
            session.Seat?.Unseat();
            session.Seat = null;
            session.Granted = ViewerRole.Spectator;
            session.Role = ViewerRole.Spectator;
            await session.SendAsync("S\t", true);
        }

        /// <summary>Moves the connection's seated unit to the pose it reports; a report from a connection that drives nothing, or with a bad number, is ignored.</summary>
        private static void ApplyPose(Session session, string line)
        {
            var unit = session.Seat;
            var fields = line.Split('\t');
            if (unit == null || fields.Length != 6
                || !TryNumber(fields[1], out var x) || !TryNumber(fields[2], out var y) || !TryNumber(fields[3], out var z) || !TryNumber(fields[4], out var yaw)
                || !int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags))
            {
                return;
            }

            unit.ApplySeatPose(new Vector3(x, y, z), yaw, flags & 7);
        }

        private static bool TryNumber(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) <= PoseLimit;
        }

        private void OnDisable()
        {
            _shutdown?.Cancel();
            _listener?.Stop();
            foreach (var client in _clients.ToArray())
            {
                client.Dispose();
            }

            _clients.Clear();
            _shutdown?.Dispose();
            _shutdown = null;
            _listener = null;
        }
    }
}
