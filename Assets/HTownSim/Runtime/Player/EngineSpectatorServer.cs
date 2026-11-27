#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>Streams Engine 07's server-owned pose to one local spectator.</summary>
    internal sealed class EngineSpectatorServer : MonoBehaviour
    {
        internal const int Port = 7778;

        private TcpListener? _listener;
        private TcpClient? _client;
        private CancellationTokenSource? _shutdown;

        internal async void Begin(AiUnit engine)
        {
            var listener = new TcpListener(IPAddress.Loopback, Port);
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

            while (!shutdown.IsCancellationRequested && engine != null)
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
                    using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = false };
                    await writer.WriteLineAsync("HTOWNSIM ENGINE07 1");

                    while (!shutdown.IsCancellationRequested && engine != null)
                    {
                        var position = engine.transform.position;
                        var yaw = engine.transform.eulerAngles.y;
                        await writer.WriteLineAsync(FormattableString.Invariant($"{position.x:R} {position.y:R} {position.z:R} {yaw:R}"));
                        await writer.FlushAsync();
                        await Task.Delay(100, shutdown);
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
