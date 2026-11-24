#nullable enable
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HTownSim.Runtime
{
    /// <summary>Owns the small driving and fire-suppression demo loop.</summary>
    [RequireComponent(typeof(CharacterController))]
    internal sealed class FireResponse : MonoBehaviour
    {
        /// <summary>Initial dispatch time in seconds.</summary>
        private const float DispatchSeconds = 180f;
        /// <summary>Maximum monitor reach in metres.</summary>
        private const float MonitorRange = 23f;
        /// <summary>References authored by the scene builder.</summary>
        [SerializeField] private Transform _incident = null!;
        [SerializeField] private ParticleSystem _fire = null!;
        [SerializeField] private ParticleSystem _smoke = null!;
        [SerializeField] private LineRenderer _water = null!;
        [SerializeField] private TMP_Text _status = null!;
        [SerializeField] private TMP_Text _telemetry = null!;
        /// <summary>Street shown in the dispatch text for the structure fire.</summary>
        [SerializeField] private string _incidentLocation = "CANAL ST";
        [SerializeField] private Light[] _beacons = null!;
        /// <summary>Candidate fire locations for free-roam calls; element 0 is the first call. Each site faces out of its storefront.</summary>
        [SerializeField] private Transform[] _incidentSites = new Transform[0];
        /// <summary>Street name for each incident site.</summary>
        [SerializeField] private string[] _incidentStreets = new string[0];
        /// <summary>The shop sign that moves with the fire, and its text.</summary>
        [SerializeField] private Transform? _sign;
        [SerializeField] private TMP_Text? _signText;
        /// <summary>Playable area (minimum and maximum X and Z); a warning shows near its edge.</summary>
        [SerializeField] private Vector2 _boundsMin;
        [SerializeField] private Vector2 _boundsMax;
        /// <summary>Seconds after containment before the next call, and the nearest a new call may be, in metres.</summary>
        /// <summary>Brightness of a lit emergency beacon.</summary>
        private const float BeaconIntensity = 160f;

        private const float RoamCallDelay = 25f;
        private const float MinimumCallDistance = 150f;
        private const float EdgeWarningDistance = 30f;
        private static readonly string[] SignNames = { "MIDTOWN MARKET", "CORNER STORE", "ELGIN DELI", "BAYOU CAFE", "HAIR AND NAILS", "TAQUERIA", "PHARMACY", "LAUNDROMAT" };
        private float _roamClock;
        private float _alertUntil;
        private int _siteIndex;
        /// <summary>Cached vehicle collision and initial pose.</summary>
        private CharacterController _controller = null!;
        private Vector3 _spawn;
        private Quaternion _spawnRotation;
        /// <summary>Current response state.</summary>
        private float _speed;
        private float _remaining = DispatchSeconds;
        private float _heat = 1f;
        private bool _paused;
        private bool _sirenEnabled;
        private bool _lightsEnabled;
        private AudioSource _siren = null!;
        private AudioClip _sirenClip = null!;
        /// <summary>Orbit rig and accumulated mouse angles.</summary>
        private CinemachineFollow _follow = null!;
        private Vector3 _initialCameraOffset;
        private float _orbitYaw;
        private float _orbitPitch;
        private bool _ownsCursor;
        /// <summary>Mouse sensitivity and safe camera elevation limits.</summary>
        private const float OrbitSensitivity = 0.15f;
        private const float MinimumPitch = 12f;
        private const float MaximumPitch = 65f;

        /// <summary>Remaining normalized fire intensity.</summary>
        internal float Heat => _heat;

        /// <summary>True while the engine can be driven: not paused, and either time remains or the fire is out (free roam).</summary>
        internal bool CanDrive => !_paused && (_remaining > 0f || _heat <= 0f);

        /// <summary>True while the response is paused.</summary>
        internal bool IsPaused => _paused;

        /// <summary>True while the player has the emergency lights switched on (manual, key L).</summary>
        internal bool LightsActive => _lightsEnabled && !_paused;

        /// <summary>True while the siren is sounding, so traffic can pull over.</summary>
        internal bool SirenActive => _sirenEnabled && !_paused;

        /// <summary>Plays dispatch and all-clear tones.</summary>
        private AudioSource _alertSource = null!;

        private AudioClip _dispatchClip = null!;

        private AudioClip _clearClip = null!;

        private AudioClip _emsClip = null!;

        /// <summary>Initializes the vehicle and a synthesized placeholder siren.</summary>
        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            _spawn = transform.position;

            _spawnRotation = transform.rotation;

            _follow = FindAnyObjectByType<CinemachineFollow>();

            _initialCameraOffset = _follow.FollowOffset;

            ResetCamera();

            _siren = gameObject.AddComponent<AudioSource>();

            const int sampleRate = 22050;

            var samples = new float[sampleRate * 4];

            var phase = 0f;

            for (var i = 0; i < samples.Length; i++)
            {
                var time = i / (float)sampleRate;

                phase += 2f * Mathf.PI * (620f + 220f * Mathf.Sin(time * Mathf.PI)) / sampleRate;

                var fade = Mathf.Min(1f, Mathf.Min(time, 4f - time) / 0.005f);

                samples[i] = Mathf.Sin(phase) * 0.15f * fade;
            }

            _sirenClip = AudioClip.Create("ApparatusWail", samples.Length, 1, sampleRate, false);

            _sirenClip.SetData(samples, 0);

            _siren.clip = _sirenClip;

            _siren.loop = true;

            _siren.volume = 0.2f;

            _siren.mute = true;

            foreach (var beacon in _beacons)
            {
                beacon.intensity = 0f;
            }

            _siren.Play();

            _alertSource = gameObject.AddComponent<AudioSource>();

            _alertSource.playOnAwake = false;

            _alertSource.spatialBlend = 0f;

            _alertSource.volume = 0.5f;

            _dispatchClip = CreateToneClip("FireDispatchBeeps", new[] { (1000f, 0.26f), (0f, 0.22f), (1000f, 0.26f), (0f, 0.22f), (1000f, 0.26f), (0f, 0.22f), (1000f, 0.26f), (0f, 0.22f), (1000f, 0.26f), (0f, 0.22f), (1000f, 0.26f) });

            _emsClip = CreateToneClip("EmsDispatchHighLow", new[] { (1400f, 0.2f), (900f, 0.2f), (1400f, 0.2f), (900f, 0.2f), (1400f, 0.2f), (900f, 0.2f), (1400f, 0.2f), (900f, 0.2f), (1400f, 0.2f), (900f, 0.2f), (1400f, 0.2f), (900f, 0.2f), (1400f, 0.2f), (900f, 0.2f), (1400f, 0.2f), (900f, 0.2f) });

            _clearClip = CreateToneClip("AllClear", new[] { (660f, 0.14f), (880f, 0.14f), (1100f, 0.26f) });
        }

        /// <summary>Sounds the first dispatch shortly after the scene starts.</summary>
        private void Start()
        {
            Invoke(nameof(PlayDispatch), 0.8f);
        }

        /// <summary>Plays the fire dispatch alert: one single tone, six beeps.</summary>
        internal void PlayDispatch()
        {
            PlayAlert(_dispatchClip);
        }

        /// <summary>Plays the EMS dispatch alert: a two-tone high/low, eight of each. Ready for medical calls.</summary>
        internal void PlayEmsDispatch()
        {
            PlayAlert(_emsClip);
        }

        /// <summary>Plays one alert at a time: a new alert replaces whatever is still sounding instead of layering on top of it.</summary>
        private void PlayAlert(AudioClip clip)
        {
            _alertSource.Stop();

            _alertSource.clip = clip;

            _alertSource.Play();
        }

        /// <summary>Plays the short all-clear chime when a fire is contained.</summary>
        private void PlayClear()
        {
            PlayAlert(_clearClip);
        }

        /// <summary>Synthesizes a sequence of sine tones (frequency 0 is a rest) with short fades to avoid clicks.</summary>
        private static AudioClip CreateToneClip(string name, (float Frequency, float Seconds)[] notes)
        {
            const int sampleRate = 22050;

            var total = 0;

            foreach (var note in notes)
            {
                total += Mathf.RoundToInt(note.Seconds * sampleRate);
            }

            var samples = new float[total];

            var index = 0;

            foreach (var note in notes)
            {
                var count = Mathf.RoundToInt(note.Seconds * sampleRate);

                for (var i = 0; i < count; i++)
                {
                    var time = i / (float)sampleRate;

                    var fade = Mathf.Min(1f, Mathf.Min(time, note.Seconds - time) / 0.01f);

                    samples[index++] = note.Frequency <= 0f ? 0f : Mathf.Sin(2f * Mathf.PI * note.Frequency * time) * 0.3f * fade;
                }
            }

            var clip = AudioClip.Create(name, total, 1, sampleRate, false);

            clip.SetData(samples, 0);

            return clip;
        }

        /// <summary>Reads controls and advances the response.</summary>
        private void Update()
        {
            UpdateCamera();

            var keyboard = Keyboard.current;

            if (keyboard is null)
            {
                return;
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                Restart();
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                _paused = !_paused;
            }

            if (keyboard.qKey.wasPressedThisFrame)
            {
                _sirenEnabled = !_sirenEnabled;
            }

            if (keyboard.lKey.wasPressedThisFrame)
            {
                _lightsEnabled = !_lightsEnabled;
            }

            _siren.mute = !_sirenEnabled || _paused || (_heat > 0f && _remaining <= 0f) || _alertSource.isPlaying;

            _water.enabled = false;

            if (CanDrive)
            {
                var throttle = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

                var steering = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);

                Drive(new Vector2(steering, throttle), keyboard.spaceKey.isPressed);

                if (_heat > 0f)
                {
                    _remaining = Mathf.Max(0f, _remaining - Time.deltaTime);
                }

                if (keyboard.eKey.isPressed && Mathf.Abs(_speed) < 0.8f)
                {
                    Suppress(Time.deltaTime);
                }
            }

            for (var i = 0; i < _beacons.Length; i++)
            {
                _beacons[i].intensity = _lightsEnabled && Mathf.Repeat(Time.time * 3f + i * 0.5f, 1f) < 0.45f ? BeaconIntensity : 0f;
            }

            if (!_paused)
            {
                AdvanceRoam(Time.deltaTime);
            }

            var distance = Vector3.Distance(transform.position, _incident.position);

            _status.text = _paused ? "RESPONSE PAUSED\nESC to resume" : _heat > 0f && Time.time < _alertUntil ? $"NEW CALL!  FIRE / {_incidentLocation}\nCheck the map and respond." : _heat <= 0f ? "FIRE CONTAINED\nGood work, Engine 43.  Free roam: drive anywhere.  R to respond again." : _remaining <= 0f ? "RESPONSE TIME EXCEEDED\nR to retry the call." : distance > MonitorRange ? $"STRUCTURE FIRE\n{_incidentLocation}.  Approach the marked storefront." : "ON SCENE\nStop, then hold E to operate the water monitor.";

            _telemetry.text = _heat <= 0f
                ? $"{Mathf.Abs(_speed) * 2.237f:00} MPH     FREE ROAM\nFIRE OUT     NEXT CALL IN {Mathf.Max(0, Mathf.CeilToInt(RoamCallDelay - _roamClock)):00}s"
                : $"{Mathf.Abs(_speed) * 2.237f:00} MPH     {distance:0} m TO INCIDENT\nFIRE {Mathf.CeilToInt(_heat * 100f):00}%     {Mathf.CeilToInt(_remaining):000}s REMAINING";

            if (!_paused && NearEdge())
            {
                _status.text = "LEAVING DISPATCH AREA\nTurn back toward the city.";
            }
        }

        /// <summary>True when the engine is within the warning distance of the edge of the playable area.</summary>
        private bool NearEdge()
        {
            if (_boundsMax.x <= _boundsMin.x || _boundsMax.y <= _boundsMin.y)
            {
                return false;
            }

            var p = transform.position;

            return p.x < _boundsMin.x + EdgeWarningDistance || p.x > _boundsMax.x - EdgeWarningDistance || p.z < _boundsMin.y + EdgeWarningDistance || p.z > _boundsMax.y - EdgeWarningDistance;
        }

        /// <summary>Counts down to the next call while the fire is out; starts a new call when the delay has passed.</summary>
        internal void AdvanceRoam(float seconds)
        {
            if (_heat > 0f || _incidentSites.Length < 2)
            {
                return;
            }

            _roamClock += Mathf.Max(0f, seconds);

            if (_roamClock >= RoamCallDelay)
            {
                StartNewCall();
            }
        }

        /// <summary>Moves the fire to a random site far enough away and starts a fresh timed response.</summary>
        private void StartNewCall()
        {
            var candidates = new List<int>();

            for (var i = 0; i < _incidentSites.Length; i++)
            {
                if (i != _siteIndex && Vector3.Distance(transform.position, _incidentSites[i].position) >= MinimumCallDistance)
                {
                    candidates.Add(i);
                }
            }

            _roamClock = 0f;

            if (candidates.Count == 0)
            {
                return;
            }

            MoveIncidentTo(candidates[Random.Range(0, candidates.Count)]);

            _heat = 1f;

            _remaining = Mathf.Clamp(Vector3.Distance(transform.position, _incident.position) / 5f + 60f, 90f, 300f);

            _alertUntil = Time.time + 6f;

            RestartFire();

            PlayDispatch();
        }

        /// <summary>Places the fire, smoke and shop sign on the given incident site.</summary>
        private void MoveIncidentTo(int index)
        {
            _siteIndex = index;

            var site = _incidentSites[index];

            _incident.SetPositionAndRotation(site.position, site.rotation);

            _fire.transform.position = site.position;

            _smoke.transform.position = site.position + new Vector3(0.6f, 6f, 0f);

            if (_sign != null)
            {
                _sign.SetPositionAndRotation(site.position + Vector3.up * 2.2f + site.forward * 0.13f, site.rotation);
            }

            if (_signText != null)
            {
                _signText.text = SignNames[index % SignNames.Length];
            }

            if (index < _incidentStreets.Length)
            {
                _incidentLocation = _incidentStreets[index];
            }

            _fire.Clear();

            _smoke.Clear();
        }

        /// <summary>Relights the fire and smoke at full strength.</summary>
        private void RestartFire()
        {
            var emission = _fire.emission;

            emission.rateOverTime = 65f;

            _fire.Play();

            _smoke.Play();
        }

        /// <summary>Orbits only while the right mouse button is held.</summary>
        private void UpdateCamera()
        {
            var mouse = Mouse.current;

            if (mouse is null || !mouse.rightButton.isPressed || !Application.isFocused)
            {
                ReleaseCursor();

                return;
            }

            Cursor.lockState = CursorLockMode.Locked;

            Cursor.visible = false;

            _ownsCursor = true;

            OrbitCamera(mouse.delta.ReadValue());
        }

        /// <summary>Applies a pixel delta to the Cinemachine follow offset.</summary>
        internal void OrbitCamera(Vector2 delta)
        {
            _orbitYaw = Mathf.Repeat(_orbitYaw + delta.x * OrbitSensitivity, 360f);

            _orbitPitch = Mathf.Clamp(_orbitPitch - delta.y * OrbitSensitivity, MinimumPitch, MaximumPitch);

            _follow.FollowOffset = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f) * Vector3.back * _initialCameraOffset.magnitude;
        }

        /// <summary>Restores the departure camera framing.</summary>
        private void ResetCamera()
        {
            _orbitYaw = 0f;

            _orbitPitch = Mathf.Atan2(_initialCameraOffset.y, -_initialCameraOffset.z) * Mathf.Rad2Deg;

            _follow.FollowOffset = _initialCameraOffset;
        }

        /// <summary>Restores the pointer after orbiting or losing focus.</summary>
        private void ReleaseCursor()
        {
            if (!_ownsCursor)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;

            Cursor.visible = true;

            _ownsCursor = false;
        }

        /// <summary>Never leaves the pointer captured after disabling the demo.</summary>
        private void OnDisable()
        {
            ReleaseCursor();
        }

        /// <summary>Moves the arcade vehicle with collision-aware steering.</summary>
        internal void Drive(Vector2 input, bool braking)
        {
            var target = braking ? 0f : input.y * (input.y >= 0f ? 13f : 5f);

            _speed = Mathf.MoveTowards(_speed, target, (braking ? 20f : 6f) * Time.deltaTime);

            transform.Rotate(0f, input.x * _speed * 4f * Time.deltaTime, 0f);

            var flags = _controller.Move((transform.forward * _speed + Vector3.down * 3f) * Time.deltaTime);

            if ((flags & CollisionFlags.Sides) != 0)
            {
                _speed = 0f;
            }
        }

        /// <summary>Applies water only within reach and with a clear line to the incident.</summary>
        internal void Suppress(float seconds)
        {
            var origin = transform.TransformPoint(new Vector3(0f, 3.15f, 0.98f));

            var offset = _incident.position - origin;

            if (_paused || _remaining <= 0f || _heat <= 0f || offset.magnitude > MonitorRange || Mathf.Abs(_speed) >= 0.8f)
            {
                return;
            }

            if (Physics.Raycast(origin, offset.normalized, out var hit, offset.magnitude - 0.8f) && hit.transform != transform)
            {
                return;
            }

            _water.enabled = true;

            for (var i = 0; i < _water.positionCount; i++)
            {
                var t = i / (float)(_water.positionCount - 1);

                _water.SetPosition(i, Vector3.Lerp(origin, _incident.position, t) + Vector3.up * (2f * Mathf.Sin(t * Mathf.PI)));
            }

            _heat = Mathf.Max(0f, _heat - Mathf.Max(0f, seconds) / 14f);

            var emission = _fire.emission;

            emission.rateOverTime = 65f * _heat;

            if (_heat <= 0f)
            {
                _fire.Stop();

                _smoke.Stop();

                PlayClear();
            }
        }

        /// <summary>Restores the dispatch without reloading other scenes.</summary>
        internal void Restart()
        {
            _controller.enabled = false;

            transform.SetPositionAndRotation(_spawn, _spawnRotation);

            _controller.enabled = true;

            _speed = 0f;

            _heat = 1f;

            _remaining = DispatchSeconds;

            _paused = false;

            _sirenEnabled = false;

            _lightsEnabled = false;

            _siren.mute = true;

            foreach (var beacon in _beacons)
            {
                beacon.intensity = 0f;
            }

            ResetCamera();

            _roamClock = 0f;

            _alertUntil = 0f;

            if (_incidentSites.Length > 0 && _siteIndex != 0)
            {
                MoveIncidentTo(0);
            }

            RestartFire();

            PlayDispatch();
        }

        /// <summary>Releases the generated audio assets.</summary>
        private void OnDestroy()
        {
            Destroy(_sirenClip);

            Destroy(_dispatchClip);

            Destroy(_clearClip);

            Destroy(_emsClip);
        }
    }
}

