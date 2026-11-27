#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>Reports that the city is ready to a local client, one response per connection.</summary>
    internal sealed class CityReadinessListener : MonoBehaviour
    {
        internal const int Port = 7777;

        // Containers opt in to binding their network interface. Desktop runs stay local.
        internal static IPAddress BindAddress => Environment.GetEnvironmentVariable("HTOWNSIM_BIND_ALL") == "1"
            ? IPAddress.Any
            : IPAddress.Loopback;

        private static readonly byte[] ReadyMessage = System.Text.Encoding.ASCII.GetBytes("HTOWNSIM READY 1\n");

        private TcpListener? _listener;

        private CancellationTokenSource? _shutdown;

        private void OnEnable()
        {
            var address = BindAddress;
            var listener = new TcpListener(address, Port);

            try
            {
                listener.Start(backlog: 8);
            }
            catch (SocketException exception)
            {
                listener.Stop();
                Debug.LogError("City readiness listener could not bind to " + address + ":" + Port + ": " + exception.Message);
                return;
            }

            _listener = listener;
            _shutdown = new CancellationTokenSource();
            Debug.Log("City readiness listener is accepting connections on " + address + ":" + Port);
            Serve(listener, _shutdown.Token);
        }

        private static async void Serve(TcpListener listener, CancellationToken shutdown)
        {
            while (!shutdown.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await listener.AcceptTcpClientAsync();
                }
                catch (Exception exception) when (exception is SocketException or ObjectDisposedException or InvalidOperationException)
                {
                    if (!shutdown.IsCancellationRequested)
                    {
                        Debug.LogError("City readiness listener stopped accepting connections: " + exception.Message);
                    }

                    return;
                }

                using (client)
                {
                    if (shutdown.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        await client.GetStream().WriteAsync(ReadyMessage, 0, ReadyMessage.Length, shutdown);
                    }
                    catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
                    {
                        // A disconnected probe should not prevent the next client from connecting.
                    }
                }
            }
        }

        private void OnDisable()
        {
            _shutdown?.Cancel();
            _listener?.Stop();
            _shutdown?.Dispose();
            _shutdown = null;
            _listener = null;
        }
    }
}
