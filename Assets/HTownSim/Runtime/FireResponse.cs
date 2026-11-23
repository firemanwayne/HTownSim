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
        [SerializeField] private GameObject? _fireVfxMain;
        [SerializeField] private GameObject? _fireVfxSpread;
        [SerializeField] private LineRenderer _water = null!;
        [SerializeField] private TMP_Text _status = null!;
        [SerializeField] private TMP_Text _telemetry = null!;
        /// <summary>Street shown in the dispatch text for the structure fire.</summary>
        [SerializeField] private string _incidentLocation = "CANAL ST";
        [SerializeField] private Light[] _beacons = null!;
        /// <summary>Recorded station ring that plays before the dispatch beeps; optional.</summary>
        [SerializeField] private AudioClip? _dispatchRing;
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
        /// <summary>Name of the dispatch panel background that hides with the incident text, and its visibility (key I).</summary>
        private const string DispatchPanelName = "DispatchPanel";
        private const string HeaderName = "HudLabel";
        private const string ControlsHintMarker = "WASD";
        private const string MapPanelName = "CityMapPanel";
        private const string HideLabelName = "InfoHideLabel";
        private const string HideLabelText = "I  HIDE";
        private const float HideLabelFontSize = 16f;
        private const float ControlsFontSize = 18f;
        private const float ControlsLineSpacing = 6f;
        private const float ControlsKeyColumn = 142f;
        private const string ControlsKeyColor = "#FFAF52";
        private static readonly string[][] ControlsList =
        {
            new[] { "WASD", "DRIVE" },
            new[] { "SPACE", "BRAKE" },
            new[] { "E (HOLD)", "WATER" },
            new[] { "RMB + DRAG", "LOOK" },
            new[] { "Q", "STANDBY" },
            new[] { "1  2  3", "WAIL  YELP  PIER" },
            new[] { "T", "NEXT TONE" },
            new[] { "H (HOLD)", "HORN" },
            new[] { "L", "LIGHTS" },
            new[] { "ESC", "PAUSE" },
            new[] { "R", "RETURN TO STATION" },
            new[] { "M", "MAP" },
            new[] { "I", "INCIDENT PANEL" },
        };
        private bool _infoVisible = true;
        /// <summary>The randomly chosen site of the first call; R retries this same incident.</summary>
        private int _firstSiteIndex;
        /// <summary>Cached vehicle collision and initial pose.</summary>
        private CharacterController _controller = null!;
        /// <summary>Traffic that reacts to collisions; registers itself at startup.</summary>
        internal TrafficSystem? Traffic { get; set; }

        /// <summary>Collision bookkeeping: the collider and point the controller last touched, the impact speed that counts as a crash and the share of speed kept after knocking a car away.</summary>
        private Collider? _lastHit;
        private Vector3 _lastHitPoint;
        private const float MinimumImpactSpeed = 1.5f;
        private const float ImpactRetainedSpeed = 0.35f;
        private const float MaximumCrashNormalY = 0.6f;
        /// <summary>Grows and spreads the fire and thickens its smoke.</summary>
        private FireSpread _spread = null!;
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
        private AudioSource _horn = null!;
        private readonly AudioClip?[] _toneClips = new AudioClip?[ToneNames.Length];
        private AudioClip? _hornClip;
        private int _tone;
        private GameObject? _incidentGroup;
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

        /// <summary>Current vehicle speed in metres per second for engine audio.</summary>
        internal float Speed => Mathf.Abs(_speed);

        /// <summary>Whether dispatch currently needs priority over environmental sound.</summary>
        internal bool DispatchPlaying => _alertSource != null && (_alertSource.isPlaying || IsInvoking(nameof(PlayDispatchBeeps)));

        /// <summary>Plays dispatch and all-clear tones.</summary>
        private AudioSource _alertSource = null!;

        private AudioClip _dispatchClip = null!;

        private AudioClip _clearClip = null!;

        private AudioClip _emsClip = null!;

        private const string SirenFolder = "SirenAudio/";
        private const string HornResource = "Horn";
        private const float SirenVolume = 0.55f;
        private const float HornVolume = 0.7f;
        private static readonly string[] ToneNames = { "WAIL", "YELP", "PHASER" };
        private static readonly string[] ToneResources = { "Wail", "Yelp", "Phaser" };

        /// <summary>Initializes the vehicle and a synthesized placeholder siren.</summary>
        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            _spread = gameObject.AddComponent<FireSpread>();

            _spread.MainPrefab = _fireVfxMain;

            _spread.SatellitePrefab = _fireVfxSpread;

            _spread.Initialize(_fire, _smoke);

            gameObject.AddComponent<WheelSpin>();

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

            LoadSirenTones();

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
            _firstSiteIndex = PickFirstSite();

            if (_incidentSites.Length > 0)
            {
                MoveIncidentTo(_firstSiteIndex);
            }

            RestartFire();

            ArrangeHud();

            AddEmergencyHud();

            gameObject.AddComponent<EngineSound>().Initialize(this);

            Invoke(nameof(PlayDispatch), 0.8f);
        }

        /// <summary>Adds the siren and light controller panel to the HUD canvas.</summary>
        private void AddEmergencyHud()
        {
            var canvas = _status.GetComponentInParent<Canvas>();

            if (canvas != null)
            {
                new GameObject("EmergencyHudDriver").AddComponent<EmergencyHud>().Initialize(this, canvas);
            }
        }

        /// <summary>Loads the recorded wail, yelp, phaser and horn clips; any missing tone falls back to the synthesized wail.</summary>
        private void LoadSirenTones()
        {
            for (var i = 0; i < ToneResources.Length; i++)
            {
                _toneClips[i] = Resources.Load<AudioClip>(SirenFolder + ToneResources[i]);
            }

            _hornClip = Resources.Load<AudioClip>(SirenFolder + HornResource);

            _horn = gameObject.AddComponent<AudioSource>();

            _horn.clip = _hornClip;

            _horn.loop = true;

            _horn.playOnAwake = false;

            _horn.spatialBlend = 0f;

            _horn.volume = HornVolume;

            ApplyTone();
        }

        /// <summary>Points the siren at the selected tone's clip, keeping it looping and muted state untouched.</summary>
        private void ApplyTone()
        {
            var clip = _toneClips[_tone];

            _siren.clip = clip != null ? clip : _sirenClip;

            _siren.volume = clip != null ? SirenVolume : 0.2f;

            _siren.loop = true;

            _siren.Play();
        }

        /// <summary>Chooses a tone and turns the siren on; choosing the active tone again, when asked, turns it off.</summary>
        internal void SelectTone(int tone, bool offIfSame)
        {
            if (offIfSame && _sirenEnabled && tone == _tone)
            {
                _sirenEnabled = false;

                return;
            }

            _tone = tone;

            _sirenEnabled = true;

            ApplyTone();
        }

        /// <summary>True while the siren is switched on (any tone), for the controller HUD.</summary>
        internal bool SirenOn => _sirenEnabled;

        /// <summary>The selected siren tone index: 0 wail, 1 yelp, 2 phaser.</summary>
        internal int Tone => _tone;

        /// <summary>Display name of the selected tone.</summary>
        internal string ToneName => ToneNames[_tone];

        /// <summary>True while the electric horn is sounding.</summary>
        internal bool HornSounding => _horn != null && _horn.isPlaying;

        /// <summary>True while the emergency lights are on.</summary>
        internal bool LightsOn => _lightsEnabled;

        /// <summary>Held by the HUD while its horn button is pressed with the mouse.</summary>
        internal bool HornPointerDown { get; set; }

        /// <summary>True when the siren and the lights are both off, which is when the STBY key is lit.</summary>
        internal bool InStandby => !_sirenEnabled && !_lightsEnabled;

        /// <summary>Puts everything in standby: siren and lights off, as the STBY key does.</summary>
        internal void StandbyAll()
        {
            _sirenEnabled = false;

            _lightsEnabled = false;
        }

        /// <summary>Toggles the emergency lights, as the L key does.</summary>
        internal void ToggleLights()
        {
            _lightsEnabled = !_lightsEnabled;
        }

        /// <summary>Starts the electric horn on key down and stops it on key up, and never while paused, dispatching or after the fire is out.</summary>
        private void UpdateHorn(UnityEngine.InputSystem.Keyboard keyboard)
        {
            if (_horn == null || _hornClip == null)
            {
                return;
            }

            var allowed = !_paused && !(_heat > 0f && _remaining <= 0f) && !DispatchPlaying;

            var wanted = keyboard.hKey.isPressed || HornPointerDown;

            if (wanted && allowed && !_horn.isPlaying)
            {
                _horn.Play();
            }

            if (_horn.isPlaying && (!wanted || !allowed))
            {
                _horn.Stop();
            }
        }

        /// <summary>Lays out the HUD: a left-hand column with the vertical key list beside a stack of the incident panel over the map.</summary>
        private void ArrangeHud()
        {
            var canvas = (RectTransform)_status.transform.parent;

            TMP_Text? controls = null;

            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.text.Contains(ControlsHintMarker))
                {
                    ArrangeControls(text);

                    controls = text;

                    break;
                }
            }

            if (controls == null)
            {
                return;
            }

            var header = canvas.Find(HeaderName);

            var hide = header != null ? CreateHideLabel(canvas, header) : null;

            _incidentGroup = HudLayout.Build(new HudLayoutParts
            {
                Canvas = canvas,
                Controls = controls,
                Panel = canvas.Find(DispatchPanelName),
                Header = header,
                Status = _status,
                Telemetry = _telemetry,
                HideLabel = hide,
                Map = canvas.Find(MapPanelName),
            });
        }

        /// <summary>Turns the baked controls line into a vertical key list beside the map, with the keys picked out in colour.</summary>
        private static void ArrangeControls(TMP_Text text)
        {
            var lines = new string[ControlsList.Length];

            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] = $"<color={ControlsKeyColor}>{ControlsList[i][0]}</color><pos={ControlsKeyColumn}>{ControlsList[i][1]}";
            }

            text.text = string.Join("\n", lines);

            text.fontSize = ControlsFontSize;

            text.lineSpacing = ControlsLineSpacing;

            text.alignment = TextAlignmentOptions.TopLeft;

            text.textWrappingMode = TextWrappingModes.NoWrap;

            text.outlineColor = new Color32(4, 10, 14, 255);

            text.outlineWidth = 0.22f;
        }

        /// <summary>Creates the "I HIDE" label for the incident panel header, styled like the map panel's "M HIDE".</summary>
        private static TMP_Text CreateHideLabel(Transform canvas, Transform header)
        {
            var clone = Instantiate(header.gameObject, canvas);

            clone.name = HideLabelName;

            var label = clone.GetComponent<TMP_Text>();

            label.text = HideLabelText;

            label.fontSize = HideLabelFontSize;

            label.fontStyle = FontStyles.Normal;

            label.color = new Color(0.902f, 0.843f, 0.718f, 1f);

            label.rectTransform.sizeDelta = new Vector2(90f, 22f);

            return label;
        }

        /// <summary>Shows or hides the dispatch panel (its background, incident status and telemetry text). Text keeps updating while hidden.</summary>
        internal void SetIncidentInfoVisible(bool visible)
        {
            _infoVisible = visible;

            if (_incidentGroup != null)
            {
                _incidentGroup.SetActive(visible);
            }
        }

        /// <summary>Chooses a random storefront at least the minimum call distance from the station for the first call; falls back to site 0.</summary>
        private int PickFirstSite()
        {
            var candidates = new List<int>();

            for (var i = 0; i < _incidentSites.Length; i++)
            {
                if (Vector3.Distance(_spawn, _incidentSites[i].position) >= MinimumCallDistance)
                {
                    candidates.Add(i);
                }
            }

            return candidates.Count == 0 ? 0 : candidates[Random.Range(0, candidates.Count)];
        }

        /// <summary>Plays the fire dispatch alert: the station ring recording first, then one single tone, six beeps. Falls back to the beeps alone when no ring is assigned.</summary>
        internal void PlayDispatch()
        {
            if (_dispatchRing == null)
            {
                PlayAlert(_dispatchClip);

                return;
            }

            PlayAlert(_dispatchRing);

            Invoke(nameof(PlayDispatchBeeps), _dispatchRing.length);
        }

        /// <summary>Plays the six dispatch beeps once the ring has finished.</summary>
        private void PlayDispatchBeeps()
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
            CancelInvoke(nameof(PlayDispatchBeeps));

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

            if (keyboard.iKey.wasPressedThisFrame)
            {
                SetIncidentInfoVisible(!_infoVisible);
            }

            if (keyboard.qKey.wasPressedThisFrame)
            {
                if (_sirenEnabled || _lightsEnabled)
                {
                    StandbyAll();
                }
                else
                {
                    _sirenEnabled = true;
                }
            }

            if (keyboard.tKey.wasPressedThisFrame)
            {
                SelectTone((_tone + 1) % ToneNames.Length, false);
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                SelectTone(0, true);
            }

            if (keyboard.digit2Key.wasPressedThisFrame)
            {
                SelectTone(1, true);
            }

            if (keyboard.digit3Key.wasPressedThisFrame)
            {
                SelectTone(2, true);
            }

            UpdateHorn(keyboard);

            if (keyboard.lKey.wasPressedThisFrame)
            {
                ToggleLights();
            }

            _siren.mute = !_sirenEnabled || _paused || (_heat > 0f && _remaining <= 0f) || DispatchPlaying;

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

            _spread.Tick(_paused ? 0f : Time.deltaTime, _heat, _water.enabled);

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
            _spread.Ignite(_incident);
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
            ThrottleInput = input.y;

            BrakeInput = braking;

            var target = braking ? 0f : input.y * (input.y >= 0f ? 13f : 5f);

            _speed = Mathf.MoveTowards(_speed, target, (braking ? 20f : 6f) * Time.deltaTime);

            transform.Rotate(0f, input.x * _speed * 4f * Time.deltaTime, 0f);

            var impactSpeed = _speed;

            _lastHit = null;

            var flags = _controller.Move((transform.forward * _speed + Vector3.down * 3f) * Time.deltaTime);

            if ((flags & CollisionFlags.Sides) == 0)
            {
                return;
            }

            // A vehicle that the traffic system knocks away costs the engine only part of its speed; anything else stops it.
            var knocked = _lastHit != null && Traffic != null && Mathf.Abs(impactSpeed) >= MinimumImpactSpeed && Traffic.TryImpact(_lastHit, _lastHitPoint, transform.forward * impactSpeed);

            _speed = knocked ? impactSpeed * ImpactRetainedSpeed : 0f;
        }

        /// <summary>Remembers what the controller last touched this frame so Drive can tell vehicles from walls.</summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // The road under the wheels reports a hit every frame; only side-facing contacts can be a crash.
            if (Mathf.Abs(hit.normal.y) > MaximumCrashNormalY)
            {
                return;
            }

            _lastHit = hit.collider;

            _lastHitPoint = hit.point;
        }

        /// <summary>Signed forward speed in metres per second (negative in reverse).</summary>
        internal float SignedSpeed => _speed;

        /// <summary>The driver's throttle this frame, -1 (reverse) to 1 (forward), for the engine sound.</summary>
        internal float ThrottleInput { get; private set; }

        /// <summary>True while the driver is braking, for the engine sound.</summary>
        internal bool BrakeInput { get; private set; }

        /// <summary>True while the response is paused, so the engine falls silent.</summary>
        internal bool EngineMuted => _paused;

        /// <summary>Scales the current speed, used when the engine ploughs through something soft.</summary>
        internal void ScaleSpeed(float factor)
        {
            _speed *= Mathf.Clamp01(factor);
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

            _heat = Mathf.Max(0f, _heat - Mathf.Max(0f, seconds) / (14f * Mathf.Sqrt(_spread.Growth)));

            _spread.Apply(_heat);

            if (_heat <= 0f)
            {
                _spread.Extinguish();

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

            if (_horn != null)
            {
                _horn.Stop();
            }

            foreach (var beacon in _beacons)
            {
                beacon.intensity = 0f;
            }

            ResetCamera();

            _roamClock = 0f;

            _alertUntil = 0f;

            if (_incidentSites.Length > 0 && _siteIndex != _firstSiteIndex)
            {
                MoveIncidentTo(_firstSiteIndex);
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

