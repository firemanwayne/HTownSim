#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Shows the city server's fleet as a spectator: every streamed unit is posed from the server, and a free camera flies over the
    /// city. F9 asks the server for a test incident.
    /// </summary>
    internal sealed class EngineSpectatorClient : MonoBehaviour
    {
        /// <summary>The unit the camera first looks at, and the one the older server stream carries alone.</summary>
        private const string EngineName = "Engine 07";

        /// <summary>The prefab the server's patrol units are copies of; the spectator has no such cars in its scene, so it makes them.</summary>
        private const string PatrolPrefab = "HpdPatrol";

        private const string PatrolPrefix = "HPD Unit";

        /// <summary>Free-fly camera speed in metres per second. Raise it to cross the city faster; lower it for finer control.</summary>
        private const float MoveSpeed = 25f;

        /// <summary>Speed multiplier while Shift is held.</summary>
        private const float FastMultiplier = 4f;

        /// <summary>Degrees of look rotation per pixel of mouse movement.</summary>
        private const float LookSensitivity = 0.12f;

        /// <summary>Lowest height the camera can reach, so it stays above the ground.</summary>
        private const float MinimumHeight = 1f;

        /// <summary>How quickly a unit eases to its latest streamed pose. Raise it to follow the server more tightly; lower it for smoother motion.</summary>
        private const float PoseSharpness = 12f;

        /// <summary>A unit this far (metres) from its streamed pose is put there at once instead of gliding.</summary>
        private const float SnapDistance = 40f;

        /// <summary>How long (seconds) a message from the server stays on screen.</summary>
        private const float MessageSeconds = 8f;

        /// <summary>A streamed unit: the scene object that shows it and where the server last put it.</summary>
        private sealed class Proxy
        {
            internal Proxy(Transform transform)
            {
                Transform = transform;
            }

            internal Transform Transform { get; }

            internal Vector3 TargetPosition { get; set; }

            internal Quaternion TargetRotation { get; set; }

            internal bool Placed { get; set; }

            /// <summary>The server's last light flags for the unit (1 lights, 2 siren).</summary>
            internal int LightFlags { get; set; }

            internal AiEmergencyLights? Lights { get; set; }

            /// <summary>The unit's radio name, which says what kind of horn it has.</summary>
            internal string UnitName { get; set; } = "";

            internal AiHorn? Horn { get; set; }
        }

        private readonly Dictionary<string, Proxy> _proxies = new Dictionary<string, Proxy>();
        private readonly HashSet<string> _unknown = new HashSet<string>();
        private readonly Dictionary<string, LiveFireVfx> _fires = new Dictionary<string, LiveFireVfx>();
        private GameObject? _firePrefab;
        private TcpClient? _connection;
        private StreamWriter? _writer;
        private GameObject[] _roots = new GameObject[0];
        private Transform? _camera;
        private double? _joinSeconds;
        private double? _beforeConnectionSeconds;
        private string _status = "Connecting to city server...";
        private string _message = "";
        private float _messageUntil;
        private float _yaw;
        private float _pitch;
        private bool _flying;
        private bool _menuWasOpen;
        private bool _hasPose;

        internal async void Begin(Transform engine, GameObject? firePrefab)
        {
            _firePrefab = firePrefab;
            _roots = engine.gameObject.scene.GetRootGameObjects();
            _beforeConnectionSeconds = CityJoinMenu.ElapsedSeconds;
            _proxies[EngineName] = new Proxy(engine) { UnitName = EngineName };
            TakeOverCamera();

            using var connection = new TcpClient();
            _connection = connection;
            try
            {
                await connection.ConnectAsync(IPAddress.Loopback, EngineSpectatorServer.Port);
                var stream = connection.GetStream();
                using var reader = new StreamReader(stream);
                _writer = new StreamWriter(stream) { AutoFlush = true };
                var header = await reader.ReadLineAsync();
                if (header != EngineSpectatorServer.Header && header != EngineSpectatorServer.LegacyHeader)
                {
                    _status = "City server protocol mismatch.";
                    return;
                }

                while (await reader.ReadLineAsync() is { } line)
                {
                    if (!Receive(line))
                    {
                        _status = "City server sent an invalid pose.";
                        return;
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
                _writer = null;
                _connection = null;
            }
        }

        /// <summary>
        /// Takes the main camera out of Cinemachine's hands: the brain would keep following the engine with its damping, which makes a
        /// free camera bounce. The camera is also taken out of any parent that moves.
        /// </summary>
        private void TakeOverCamera()
        {
            var main = Camera.main;
            var brain = FindAnyObjectByType<CinemachineBrain>();
            if (main == null && brain != null)
            {
                main = brain.GetComponent<Camera>();
            }

            if (main == null)
            {
                return;
            }

            if (brain != null)
            {
                brain.enabled = false;
            }

            main.transform.SetParent(null, true);
            _camera = main.transform;
        }

        /// <summary>Handles one line from the server; false when it cannot be read.</summary>
        private bool Receive(string line)
        {
            var fields = line.Split('\t');
            if (fields.Length == 2 && fields[0] == "M")
            {
                _message = fields[1];
                _messageUntil = Time.unscaledTime + MessageSeconds;

                return true;
            }

            if (fields.Length == 2 && fields[0] == "C")
            {
                RemoveFire(fields[1]);

                return true;
            }

            if (fields.Length == 8 && fields[0] == "F")
            {
                if (!TryPose(fields[2], fields[3], fields[4], "0", out var firePosition, out _)
                    || !TryRead(fields[5], out var heat) || !TryRead(fields[6], out var growth))
                {
                    return false;
                }

                ShowFire(fields[1], firePosition, heat, growth, fields[7] == "1");

                return true;
            }

            // A unit line has the flags as a seventh field; the earlier server left them out.
            if ((fields.Length == 6 || fields.Length == 7) && fields[0] == "U")
            {
                var flags = 0;

                return TryPose(fields[2], fields[3], fields[4], fields[5], out var position, out var yaw)
                    && (fields.Length == 6 || int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out flags))
                    && Place(fields[1], position, yaw, flags);
            }

            // The older stream: Engine 07 alone, four numbers separated by spaces.
            var legacy = line.Split(' ');

            return legacy.Length == 4
                && TryPose(legacy[0], legacy[1], legacy[2], legacy[3], out var legacyPosition, out var legacyYaw)
                && Place(EngineName, legacyPosition, legacyYaw, 0);
        }

        /// <summary>Shows or updates a fire the server reports: made the first time it is seen, then set to the server's heat and growth.</summary>
        private void ShowFire(string id, Vector3 position, float heat, float growth, bool isOut)
        {
            if (_firePrefab == null)
            {
                return;
            }

            if (!_fires.TryGetValue(id, out var fire))
            {
                fire = new LiveFireVfx(_firePrefab, position, transform);
                _fires[id] = fire;
            }

            fire.Set(heat, growth);
            if (isOut)
            {
                fire.Stop();
            }
        }

        private void RemoveFire(string id)
        {
            if (_fires.TryGetValue(id, out var fire))
            {
                fire.Destroy();
                _fires.Remove(id);
            }
        }

        private static bool TryPose(string x, string y, string z, string yaw, out Vector3 position, out float angle)
        {
            position = default;
            angle = 0f;

            if (!TryRead(x, out var px) || !TryRead(y, out var py) || !TryRead(z, out var pz) || !TryRead(yaw, out angle))
            {
                return false;
            }

            position = new Vector3(px, py, pz);

            return true;
        }

        /// <summary>Sets where the server has a unit; the first pose of Engine 07 also starts the camera.</summary>
        private bool Place(string unit, Vector3 position, float yaw, int lightFlags)
        {
            var proxy = ProxyFor(unit);
            if (proxy == null)
            {
                return true;
            }

            ApplyLights(proxy, lightFlags);

            proxy.TargetPosition = position;
            proxy.TargetRotation = Quaternion.Euler(0f, yaw, 0f);
            if (!proxy.Placed)
            {
                proxy.Transform.SetPositionAndRotation(proxy.TargetPosition, proxy.TargetRotation);
                proxy.Placed = true;
            }

            if (!_hasPose && unit == EngineName)
            {
                _hasPose = true;
                _joinSeconds = CityJoinMenu.ElapsedSeconds;
                _status = "Spectating the city";
                StartFlying(position, proxy.TargetRotation);
            }

            return true;
        }

        /// <summary>Mirrors the server's lights and siren on a unit: the light component is added the first time a unit needs it.</summary>
        private static void ApplyLights(Proxy proxy, int flags)
        {
            if (flags == proxy.LightFlags)
            {
                return;
            }

            var before = proxy.LightFlags;
            proxy.LightFlags = flags;

            // The horn follows the server's (bit 4); the unit's kind picks the air horn or the electric one.
            if ((flags & 4) != 0 && proxy.Horn == null)
            {
                proxy.Horn = proxy.Transform.gameObject.GetComponent<AiHorn>();
                if (proxy.Horn == null)
                {
                    var profile = ApparatusLoadout.ProfileFor(proxy.UnitName);
                    proxy.Horn = proxy.Transform.gameObject.AddComponent<AiHorn>();
                    proxy.Horn.Configure(profile.AirHorn, profile.Behavior.HornVolume);
                }
            }

            proxy.Horn?.SetHeld((flags & 4) != 0);

            if ((before & 3) == (flags & 3))
            {
                return;
            }

            if (proxy.Lights == null)
            {
                proxy.Lights = proxy.Transform.gameObject.GetComponent<AiEmergencyLights>();
                if (proxy.Lights == null)
                {
                    proxy.Lights = proxy.Transform.gameObject.AddComponent<AiEmergencyLights>();
                    proxy.Lights.Configure(UnitBehavior.For(proxy.UnitName));
                }
            }

            proxy.Lights.Set((flags & 1) != 0, (flags & 2) != 0);
        }

        /// <summary>The scene object that shows a streamed unit: the parked apparatus of that name, or a patrol car made for it; null for an unknown unit.</summary>
        private Proxy? ProxyFor(string unit)
        {
            if (_proxies.TryGetValue(unit, out var known))
            {
                return known;
            }

            if (_unknown.Contains(unit))
            {
                return null;
            }

            var found = AiFleet.FindParkedObject(_roots, unit);
            if (found == null && unit.StartsWith(PatrolPrefix, StringComparison.Ordinal))
            {
                var prefab = Resources.Load<GameObject>(PatrolPrefab);
                if (prefab != null)
                {
                    var car = Instantiate(prefab);
                    car.name = "Spectator " + unit;
                    found = car.transform;
                }
            }

            if (found == null)
            {
                _unknown.Add(unit);

                return null;
            }

            var proxy = new Proxy(found) { UnitName = unit };
            _proxies[unit] = proxy;

            return proxy;
        }

        private static bool TryRead(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                && !float.IsNaN(result) && !float.IsInfinity(result);
        }

        private void Update()
        {
            if (HandleMenu())
            {
                return;
            }

            var blend = 1f - Mathf.Exp(-PoseSharpness * Time.unscaledDeltaTime);
            foreach (var proxy in _proxies.Values)
            {
                if (!proxy.Placed)
                {
                    continue;
                }

                var transform = proxy.Transform;
                if ((transform.position - proxy.TargetPosition).sqrMagnitude > SnapDistance * SnapDistance)
                {
                    transform.SetPositionAndRotation(proxy.TargetPosition, proxy.TargetRotation);

                    continue;
                }

                transform.SetPositionAndRotation(
                    Vector3.Lerp(transform.position, proxy.TargetPosition, blend),
                    Quaternion.Slerp(transform.rotation, proxy.TargetRotation, blend));
            }

            if (Keyboard.current is { } keyboard && keyboard.f9Key.wasPressedThisFrame && _writer != null)
            {
                _writer.WriteLine("TEST");
                _message = "Asking the server for a test fire...";
                _messageUntil = Time.unscaledTime + MessageSeconds;
            }

            Fly();
        }

        /// <summary>
        /// U opens the start screen, where picking a unit leaves the spectator view. Returns true while the screen is open, so the camera
        /// does not move under it; the mouse is captured again once it closes.
        /// </summary>
        private bool HandleMenu()
        {
            if (StartingTruckMenu.IsOpen)
            {
                _menuWasOpen = true;

                return true;
            }

            if (_menuWasOpen)
            {
                _menuWasOpen = false;

                if (_flying)
                {
                    SetCursorCaptured(true);
                }
            }

            if (Keyboard.current is { } keyboard && keyboard.uKey.wasPressedThisFrame)
            {
                SetCursorCaptured(false);
                StartingTruckMenu.OpenFromSpectator();

                return true;
            }

            return false;
        }

        /// <summary>Puts the camera behind and above Engine 07, looking at it, and hands control to the keyboard and mouse.</summary>
        private void StartFlying(Vector3 position, Quaternion rotation)
        {
            if (_camera == null)
            {
                return;
            }

            _camera.position = position + rotation * new Vector3(0f, 6f, -16f);
            _camera.rotation = Quaternion.LookRotation(position + Vector3.up * 2f - _camera.position);
            var euler = _camera.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            _flying = true;
            SetCursorCaptured(true);
        }

        /// <summary>Locks and hides the cursor so mouse movement turns the camera; Esc frees it, a click captures it again.</summary>
        private static void SetCursorCaptured(bool captured)
        {
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }

        /// <summary>WASD moves, E raises, Q lowers, Shift multiplies the speed, and the mouse looks around while the cursor is captured.</summary>
        private void Fly()
        {
            var keyboard = Keyboard.current;
            if (!_flying || _camera == null || keyboard is null)
            {
                return;
            }

            var mouse = Mouse.current;
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                SetCursorCaptured(false);
            }
            else if (mouse is not null && mouse.leftButton.wasPressedThisFrame)
            {
                SetCursorCaptured(true);
            }

            if (mouse is not null && Cursor.lockState == CursorLockMode.Locked)
            {
                var look = mouse.delta.ReadValue() * LookSensitivity;
                _yaw += look.x;
                _pitch = Mathf.Clamp(_pitch - look.y, -89f, 89f);
            }

            _camera.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            var move = Vector3.zero;
            move += _camera.forward * ((keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
            move += _camera.right * ((keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f));
            move += Vector3.up * ((keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f));
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            var fast = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            var position = _camera.position + move * (MoveSpeed * (fast ? FastMultiplier : 1f) * Time.unscaledDeltaTime);
            position.y = Mathf.Max(position.y, MinimumHeight);
            _camera.position = position;
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(16f, 16f, 340f, 35f), _status);
            if (_joinSeconds is double total && _beforeConnectionSeconds is double beforeConnection)
            {
                GUI.Box(new Rect(16f, 57f, 440f, 35f),
                    $"Join to first pose: {total:0.00}s   |   Before connection: {beforeConnection:0.00}s");
            }

            if (_flying)
            {
                GUI.Box(new Rect(16f, 98f, 700f, 35f),
                    "WASD move   E up   Q down   Shift x4   Mouse look   M map   F9 test fire   Esc free mouse   U pick unit");
            }

            if (_message.Length > 0 && Time.unscaledTime < _messageUntil)
            {
                GUI.Box(new Rect(16f, 139f, 700f, 35f), _message);
            }
        }

        private void OnDisable()
        {
            if (_flying)
            {
                SetCursorCaptured(false);
            }

            foreach (var fire in _fires.Values)
            {
                fire.Destroy();
            }

            _fires.Clear();
            _connection?.Dispose();
        }
    }
}
