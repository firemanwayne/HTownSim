#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Unity.Cinemachine;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>Shows Engine 07 using poses received from the city server.</summary>
    internal sealed class EngineSpectatorClient : MonoBehaviour
    {
        private TcpClient? _connection;
        private Transform? _engine;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private bool _hasPose;
        private string _status = "Connecting to city server...";

        internal async void Begin(Transform engine, CinemachineCamera? camera)
        {
            _engine = engine;
            if (camera != null)
            {
                camera.Follow = engine;
            }

            using var connection = new TcpClient();
            _connection = connection;
            try
            {
                await connection.ConnectAsync(IPAddress.Loopback, EngineSpectatorServer.Port);
                using var reader = new StreamReader(connection.GetStream());
                if (await reader.ReadLineAsync() != "HTOWNSIM ENGINE07 1")
                {
                    _status = "City server protocol mismatch.";
                    return;
                }

                while (await reader.ReadLineAsync() is { } line)
                {
                    var fields = line.Split(' ');
                    if (fields.Length != 4
                        || !TryRead(fields[0], out var x)
                        || !TryRead(fields[1], out var y)
                        || !TryRead(fields[2], out var z)
                        || !TryRead(fields[3], out var yaw))
                    {
                        _status = "City server sent an invalid pose.";
                        return;
                    }

                    _targetPosition = new Vector3(x, y, z);
                    _targetRotation = Quaternion.Euler(0f, yaw, 0f);
                    if (!_hasPose && _engine != null)
                    {
                        _engine.SetPositionAndRotation(_targetPosition, _targetRotation);
                        _hasPose = true;
                        _status = "Spectating Engine 07";
                    }
                }

                _status = "Disconnected from city server.";
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                _status = "City server unavailable at 127.0.0.1:" + EngineSpectatorServer.Port;
            }
            finally
            {
                _connection = null;
            }
        }

        private static bool TryRead(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                && !float.IsNaN(result) && !float.IsInfinity(result);
        }

        private void Update()
        {
            if (!_hasPose || _engine == null)
            {
                return;
            }

            var blend = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            _engine.SetPositionAndRotation(
                Vector3.Lerp(_engine.position, _targetPosition, blend),
                Quaternion.Slerp(_engine.rotation, _targetRotation, blend));
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(16f, 16f, 340f, 35f), _status);
        }

        private void OnDisable()
        {
            _connection?.Dispose();
        }
    }
}
