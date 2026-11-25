#nullable enable
using System.Collections.Generic;
using System.Linq;
using TMPro;
using HTownSim.Runtime.Incidents.Managers;
using HTownSim.Runtime.Incidents.Models;
using HTownSim.Runtime.Incidents.Sources;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HTownSim.Runtime
{
    /// <summary>Owns the small driving and fire-suppression demo loop.</summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-10000)]
    internal sealed class FireResponse : MonoBehaviour
    {
        /// <summary>Initial dispatch time in seconds.</summary>
        private const float DispatchSeconds = 180f;
        /// <summary>Maximum monitor reach in metres.</summary>
        private const float MonitorRange = 23f;
        /// <summary>Reach of the aerial's master stream, measured from the ladder tip (fully raised and extended the tip can be 30+ m from a fire).</summary>
        private const float AerialStreamRange = 50f;
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
        private const float BeaconHertz = 1.15f;
        private const float BeaconDuty = 0.45f;
        private const float BeaconSpread = 0.07f;
        private const float BeaconSpreadStep = 0.6180339f;

        private const float RoamCallDelay = 25f;
        private const float MinimumCallDistance = 150f;
        private const float EdgeWarningDistance = 30f;
        private static readonly string[] SignNames = { "MIDTOWN MARKET", "CORNER STORE", "ELGIN DELI", "BAYOU CAFE", "HAIR AND NAILS", "TAQUERIA", "PHARMACY", "LAUNDROMAT" };
        private float _roamClock;
        private float _alertUntil;
        private int _siteIndex;
        /// <summary>A call from an outside source waits this long after the last one clears before it is dispatched.</summary>
        private const float ExternalCallSettleSeconds = 3f;
        /// <summary>A call this close to a storefront site burns at that site (with its sign); further away it burns at the exact spot.</summary>
        private const float SiteSnapMeters = 30f;
        /// <summary>Seconds a unit must stay stopped at a non-fire call to clear it.</summary>
        private const float SceneSeconds = 8f;
        /// <summary>The one door every call comes through: standard incident JSON in, queued calls out.</summary>
        private IncidentManager _incidents = null!;

        /// <summary>The live City of Houston incident feed (harvester files to the board and to dispatches).</summary>
        private LiveIncidentFeed? _liveFeed;
        /// <summary>The call being worked, and the first call of the session (which R retries).</summary>
        private Incident? _activeIncident;
        private Incident? _firstIncident;
        /// <summary>The patient lying at an EMS call, created when the first one is needed.</summary>
        private DownedCitizen? _patient;
        /// <summary>How far in front of the storefront the patient lies, and how long they stay after the call clears.</summary>
        private const float PatientFrontOffset = 2.5f;
        private const float PatientClearSeconds = 6f;
        /// <summary>True once the active call's outcome has been reported to the manager.</summary>
        private bool _outcomeReported;

        /// <summary>The emergency-room bays of the Texas Medical Center hospitals (objects named ER_Bay_*), found at start.</summary>
        private Transform[] _hospitals = new Transform[0];

        /// <summary>True while the patient is aboard and the incident marker sits at the hospital bay.</summary>
        private bool _transporting;

        /// <summary>The hospital the patient is being taken to.</summary>
        private string _hospitalName = string.Empty;
        /// <summary>True when the active call is a fire, or when there is no call record yet.</summary>
        private bool IncidentIsFire => _activeIncident is null || _activeIncident.IsFire;
        /// <summary>Name of the dispatch panel background that hides with the incident text, and its visibility (key I).</summary>
        private const string DispatchPanelName = "DispatchPanel";
        private const string HeaderName = "HudLabel";
        private const string ControlsHintMarker = "WASD";
        /// <summary>Start of the HUD title bar text; the unit name follows it.</summary>
        private const string TitlePrefix = "H T O W N   /   ";
        private const string MapPanelName = "CityMapPanel";
        private const string HideLabelName = "InfoHideLabel";
        private const string HideLabelText = "I  HIDE";
        private const float HideLabelFontSize = 16f;
        private const float ControlsFontSize = 18f;
        private const float ControlsLineSpacing = 6f;
        private const float ControlsKeyColumn = 142f;
        private const string ControlsKeyColor = "#FFAF52";
        private const string WaterControl = "WATER";

        /// <summary>The control list only shows while this key is held (it is off otherwise), and lists just what the driven vehicle has.</summary>
        private const string ControlsKeyLabel = "TAB (HOLD)";

        /// <summary>Keys of the aerial-ladder rows, listed only for an apparatus with an aerial (Ladder 07).</summary>
        private static readonly string[] AerialControlKeys = { "4  5", "6  7", "UP  DOWN", "]  [", "LEFT  RIGHT" };
        private static readonly string[][] ControlsList =
        {
            new[] { ControlsKeyLabel, "CONTROLS" },
            new[] { "WASD", "DRIVE" },
            new[] { "SPACE", "BRAKE" },
            new[] { "E (HOLD)", WaterControl },
            new[] { "RMB + DRAG", "LOOK" },
            new[] { "WHEEL", "ZOOM" },
            new[] { "Q", "STANDBY" },
            new[] { "1  2  3", "WAIL  YELP  PIER" },
            new[] { "T", "NEXT TONE" },
            new[] { "H (HOLD)", "HORN" },
            new[] { "L", "LIGHTS" },
            new[] { "F", "SCENE LIGHTS" },
            new[] { "4  5", "OUTRIGGERS OUT / IN" },
            new[] { "6  7", "OUTRIGGERS DOWN / UP" },
            new[] { "UP  DOWN", "LADDER RAISE / LOWER" },
            new[] { "]  [", "LADDER EXTEND / RETRACT" },
            new[] { "LEFT  RIGHT", "LADDER ROTATE" },
            new[] { "Z  X  C", "LEFT  HAZARD  RIGHT" },
            new[] { "ESC", "PAUSE" },
            new[] { "R", "RETURN TO STATION" },
            new[] { "U", "CHANGE UNIT" },
            new[] { "M", "MAP" },
            new[] { "I", "INCIDENT PANEL" },
        };
        private bool _infoVisible = true;
        /// <summary>The randomly chosen site of the first call; R retries this same incident.</summary>
        private int _firstSiteIndex;
        /// <summary>Cached vehicle collision and initial pose.</summary>
        private CharacterController _controller = null!;
        /// <summary>The driven apparatus's fit-out: wheels, turning, camera distance, water origin, lights.</summary>
        private ApparatusProfileValues _profile = ApparatusProfile.Engine;

        /// <summary>The driven apparatus's fit-out, for the other apparatus components.</summary>
        internal ApparatusProfileValues Profile => _profile;

        /// <summary>The roof beacon lights, which <see cref="ApparatusLoadout"/> moves when it changes the apparatus.</summary>
        internal Light[] Beacons => _beacons;

        /// <summary>The aerial, when the driven apparatus has one (Ladder 07); the truck holds still unless it is stowed.</summary>
        private AerialController? _aerial;

        /// <summary>True while the aerial's outriggers or ladder are moving, so the engine revs to drive the hydraulic pump.</summary>
        internal bool AerialHydraulicsWorking => _aerial != null && _aerial.Working;

        /// <summary>True while an aerial is set up (outriggers out or ladder raised), so the truck may not drive.</summary>
        internal bool AerialDeployed => _aerial != null && !_aerial.Stowed;
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

        /// <summary>The key list, shown only while the controls key is held.</summary>
        private TMP_Text? _controlsText;
        private float[] _beaconPhase = new float[0];
        private bool _leftSignal;
        private bool _rightSignal;
        private bool _hazards;
        private ApparatusLights? _apparatus;
        /// <summary>Orbit rig and accumulated mouse angles.</summary>
        private CinemachineFollow _follow = null!;
        private Vector3 _initialCameraOffset;
        private float _orbitYaw;
        private float _orbitPitch;
        private bool _ownsCursor;
        /// <summary>Mouse sensitivity and safe camera elevation limits.</summary>
        private const float OrbitSensitivity = 0.07f;
        private const float MinimumPitch = 12f;
        private const float MaximumPitch = 65f;
        /// <summary>Mouse-wheel zoom as a multiple of the authored framing (15 m behind and above the truck): out to 4x
        /// (about 61 m); in, the camera closes to <see cref="MinimumDistanceZoom"/> (about 6 m, still clear of the 9.4 m
        /// truck) and below that the lens narrows instead (telephoto, down to half the field of view) so the camera never
        /// ends up inside the engine. Each notch changes it by <see cref="ZoomPerNotch"/> (multiplicative, so every notch
        /// feels the same near and far); the camera eases there at <see cref="ZoomEase"/>.</summary>
        private const float MinimumZoom = 0.2f;
        private const float MaximumZoom = 4f;
        private const float MinimumDistanceZoom = 0.4f;
        private const float ZoomPerNotch = 0.15f;
        private const float ZoomEase = 12f;
        private CinemachineCamera? _followCamera;
        private float _defaultFieldOfView;
        /// <summary>Raw wheel units per notch on Windows; newer Input System settings report about 1 per notch instead.</summary>
        private const float WheelUnitsPerNotch = 120f;
        private float _zoomTarget = 1f;
        private float _zoom = 1f;

        /// <summary>Remaining normalized fire intensity.</summary>
        internal float Heat => _heat;

        /// <summary>True while a call is in progress: a fire still burning, or any other call (medical, accident, ...) not yet cleared or failed. The city map marks the scene while this is true.</summary>
        internal bool IncidentActive => _activeIncident is not null ? !_outcomeReported : _heat > 0f;

        /// <summary>True while the engine can be driven: not paused, and either time remains or the fire is out (free roam).</summary>
        internal bool CanDrive => !_paused && (_remaining > 0f || _heat <= 0f);

        /// <summary>True while the response is paused.</summary>
        internal bool IsPaused => _paused;

        /// <summary>True while the player has the emergency lights switched on (manual, key L).</summary>
        internal bool LightsActive => _lightsEnabled && !_paused;

        /// <summary>True while the left turn signal or the hazard flashers are on.</summary>
        internal bool LeftSignalOn => (_leftSignal || _hazards) && !_paused;

        /// <summary>True while the right turn signal or the hazard flashers are on.</summary>
        internal bool RightSignalOn => (_rightSignal || _hazards) && !_paused;

        /// <summary>True while the headlights (low beams and amber clearance lights) are on. They follow the emergency lights for now; this is the one place to give them their own switch.</summary>
        internal bool HeadlightsActive => _lightsEnabled && !_paused;

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
        /// <summary>The synthesized air horn's start and hold-loop clips (ArtSource/Audio/make_air_horn.py).</summary>
        private const string AirHornStartResource = "AirHornStart";
        private const string AirHornLoopResource = "AirHornLoop";        private AirHorn? _airHorn;
        private const float SirenVolume = 0.55f;
        private const float HornVolume = 0.7f;
        private static readonly string[] ToneNames = { "WAIL", "YELP", "PHASER" };
        private static readonly string[] ToneResources = { "Wail", "Yelp", "Phaser" };

        /// <summary>Initializes the vehicle and a synthesized placeholder siren.</summary>
        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            // Fit the apparatus picked on the start screen before anything reads the profile or the bay position.
            var picked = StartingTruckChoice.Saved;

            if (picked is not null)
            {
                ApparatusLoadout.Fit(new LoadoutRequest(this, picked.Value));
            }

            _profile = ApparatusProfile.For(gameObject);

            _spread = gameObject.AddComponent<FireSpread>();

            _spread.MainPrefab = _fireVfxMain;

            _spread.SatellitePrefab = _fireVfxSpread;

            _spread.Initialize(_fire, _smoke);

            gameObject.AddComponent<WheelSpin>();

            _spawn = transform.position;

            _spawnRotation = transform.rotation;

            _follow = FindAnyObjectByType<CinemachineFollow>();

            _initialCameraOffset = _follow.FollowOffset * _profile.CameraDistanceScale;

            _followCamera = _follow.GetComponent<CinemachineCamera>();

            _defaultFieldOfView = _followCamera != null ? _followCamera.Lens.FieldOfView : 0f;

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

            // The two beacon point lights only throw coloured bounce light on walls and asphalt; the LED modules themselves are emissive
            // lenses picked up by Bloom. Shadows stay on the sun (and headlights) alone.
            foreach (var beacon in _beacons)
            {
                beacon.intensity = 0f;

                beacon.shadows = LightShadows.None;
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
            _hospitals = FindHospitals();

            _firstSiteIndex = PickFirstSite();

            _incidents = gameObject.AddComponent<IncidentManager>();

            var inbox = System.IO.Path.Combine(Application.persistentDataPath, "Incidents");

            _incidents.Initialize(new IncidentManagerSettings(PlayableArea(), inbox));

            _liveFeed = gameObject.AddComponent<LiveIncidentFeed>();

            _liveFeed.Initialize(new LiveFeedSetup(_incidents, transform, PlayableArea(), inbox));

            // Other units' EMS calls on the board get a patient lying at the scene too.
            gameObject.AddComponent<LiveIncidentPatients>().Initialize(_liveFeed, _controller);

            // Police calls on the board get an HPD patrol unit parked at the scene, lightbar flashing.
            gameObject.AddComponent<LiveIncidentPolice>().Initialize(_liveFeed);

            // No start-up call: the unit starts in service and waits for a dispatch (the live feed, or a fallback call after the roam delay).
            GoInService();

            ArrangeHud();

            AddEmergencyHud();

            var boardCanvas = _status.GetComponentInParent<Canvas>();

            if (_liveFeed != null && boardCanvas != null)
            {
                new GameObject("ActiveIncidentsHud").AddComponent<ActiveIncidentsHud>().Initialize(_liveFeed, boardCanvas);
            }

            gameObject.AddComponent<EngineSound>().Initialize(this);

            if (_profile.RearLamps)
            {
                gameObject.AddComponent<RearLamps>().Initialize(this);
            }

            gameObject.AddComponent<SceneLights>().Initialize(this);

            // Blinkers, brake and reverse lamps modelled in the mesh (District Chief 08); removes itself on a model without them.
            gameObject.AddComponent<SignalLamps>().Initialize(this);

            _aerial = AerialController.Create(this);

            var hudCanvas = _status.GetComponentInParent<Canvas>();

            if (_aerial != null && hudCanvas != null)
            {
                new GameObject("AerialHudDriver").AddComponent<AerialHud>().Initialize(_aerial, hudCanvas, _emergencyHud);
            }

            if (_profile.FlashGroupA.Length + _profile.FlashGroupB.Length + _profile.SteadyLenses.Length > 0)
            {
                gameObject.AddComponent<LensFlasher>().Initialize(this, _profile);
            }

        }

        /// <summary>No call: fire and smoke out, the clock stopped, waiting for the next dispatch.</summary>
        private void GoInService()
        {
            _heat = 0f;

            _remaining = 0f;

            _spread.Extinguish();

            _fire.Clear();

            _smoke.Clear();
        }

        /// <summary>Adds the siren and light controller panel to the HUD canvas.</summary>
        private void AddEmergencyHud()
        {
            var canvas = _status.GetComponentInParent<Canvas>();

            // A citizen car has no siren or light controls to show.
            if (canvas != null && _profile.EmergencyEquipment)
            {
                _emergencyHud = new GameObject("EmergencyHudDriver").AddComponent<EmergencyHud>();

                _emergencyHud.Initialize(this, canvas);
            }
        }

        /// <summary>The siren and light controller panel, which the aerial panel replaces while the outriggers are set.</summary>
        private EmergencyHud? _emergencyHud;

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

            // The synthesized air horn (start + seamless loop + release), when its clips are present.
            var airStart = Resources.Load<AudioClip>(SirenFolder + AirHornStartResource);

            var airLoop = Resources.Load<AudioClip>(SirenFolder + AirHornLoopResource);

            // Engine and ladder have the air horn; the ambulance (profile AirHorn off) keeps the original Horn.mp3 above, the electric horn.
            if (_profile.AirHorn && airStart != null && airLoop != null)
            {
                _airHorn = gameObject.AddComponent<AirHorn>();

                _airHorn.Initialize(airStart, airLoop, HornVolume);
            }

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
            // A citizen car has no siren.
            if (!_profile.EmergencyEquipment)
            {
                return;
            }

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
        internal bool HornSounding => _airHorn != null ? _airHorn.Held : _horn != null && _horn.isPlaying;

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
            // A citizen car has no emergency lights.
            if (!_profile.EmergencyEquipment)
            {
                return;
            }

            _lightsEnabled = !_lightsEnabled;
        }

        /// <summary>True while the front scene lights are switched on (F key).</summary>
        internal bool SceneLightsOn { get; private set; }

        /// <summary>Toggles the front scene lights, as the F key does.</summary>
        internal void ToggleSceneLights()
        {
            SceneLightsOn = !SceneLightsOn;
        }

        /// <summary>Sounds the horn while H (or the HUD's HORN key) is held, and never while paused, dispatching or after the fire is out.</summary>
        private void UpdateHorn(UnityEngine.InputSystem.Keyboard keyboard)
        {
            var allowed = !_paused && !(_heat > 0f && _remaining <= 0f) && !DispatchPlaying;

            var wanted = keyboard.hKey.isPressed || HornPointerDown;

            // The air horn: blows while held, fades out when let go.
            if (_airHorn != null)
            {
                if (wanted && allowed)
                {
                    _airHorn.Press();
                }
                else
                {
                    _airHorn.Release();
                }

                return;
            }

            if (_horn == null || _hornClip == null)
            {
                return;
            }

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
                // The title bar names the unit being driven ("H T O W N   /   LADDER 07").
                // The map panel has its own "H T O W N   /   CITY MAP" header, which keeps its text.
                if (text.text.StartsWith(TitlePrefix) && text.transform.parent.name != MapPanelName)
                {
                    text.text = TitlePrefix + _profile.UnitName.ToUpperInvariant();
                }
            }

            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.text.Contains(ControlsHintMarker))
                {
                    ArrangeControls(text, _profile.CanSprayWater, HasAerial());

                    _controlsText = text;

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

            // Off until the key is held.
            controls.enabled = false;
        }

        /// <summary>True when the driven model has an aerial ladder (a turntable under it).</summary>
        private bool HasAerial()
        {
            foreach (var child in GetComponentsInChildren<Transform>(false))
            {
                if (child.name == "CTRL_Turntable")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Turns the baked controls line into a vertical key list beside the map, with the keys picked out in colour.</summary>
        private static void ArrangeControls(TMP_Text text, bool canSprayWater, bool hasAerial)
        {
            var lines = ControlsList
                .Where(row => (canSprayWater || row[1] != WaterControl) && (hasAerial || System.Array.IndexOf(AerialControlKeys, row[0]) < 0))
                .Select(row => $"<color={ControlsKeyColor}>{row[0]}</color><pos={ControlsKeyColumn}>{row[1]}")
                .ToArray();

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
            // An EMS call gets its own tone (no station ring): the two-tone high/low. With no call on record the unit decides.
            if (_activeIncident is not null ? _activeIncident.Agency == IncidentAgency.Ems : _profile.EmsAlert)
            {
                PlayEmsDispatch();

                return;
            }

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

        /// <summary>Plays the EMS dispatch alert: a two-tone high/low, eight of each, with no station ring. Used by EMS units (the ambulance).</summary>
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

            if (keyboard.uKey.wasPressedThisFrame)
            {
                StartingTruckMenu.Reopen();
            }

            if (keyboard.iKey.wasPressedThisFrame)
            {
                SetIncidentInfoVisible(!_infoVisible);
            }

            if (_controlsText != null)
            {
                _controlsText.enabled = keyboard.tabKey.isPressed;
            }

            if (keyboard.qKey.wasPressedThisFrame && _profile.EmergencyEquipment)
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

            if (keyboard.zKey.wasPressedThisFrame)
            {
                _leftSignal = !_leftSignal;

                _rightSignal = false;

                _hazards = false;
            }

            if (keyboard.cKey.wasPressedThisFrame)
            {
                _rightSignal = !_rightSignal;

                _leftSignal = false;

                _hazards = false;
            }

            if (keyboard.xKey.wasPressedThisFrame)
            {
                _hazards = !_hazards;

                _leftSignal = false;

                _rightSignal = false;
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

            if (keyboard.fKey.wasPressedThisFrame)
            {
                ToggleSceneLights();
            }

            _siren.mute = !_sirenEnabled || _paused || (_heat > 0f && _remaining <= 0f) || DispatchPlaying;

            _water.enabled = false;

            if (CanDrive)
            {
                var throttle = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

                var steering = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);

                // With the outriggers out or the ladder up the truck holds still (brakes on, no throttle or steering).
                if (AerialDeployed)
                {
                    Drive(Vector2.zero, true);
                }
                else
                {
                    Drive(new Vector2(steering, throttle), keyboard.spaceKey.isPressed);
                }

                if (_heat > 0f)
                {
                    _remaining = Mathf.Max(0f, _remaining - Time.deltaTime);

                    if (_remaining <= 0f)
                    {
                        ReportOutcome(IncidentOutcome.Failed);
                    }
                }

                WorkNonFireScene(Time.deltaTime);

                if (_profile.CanSprayWater && keyboard.eKey.isPressed && Mathf.Abs(_speed) < 0.8f)
                {
                    Suppress(Time.deltaTime);
                }
            }

            if (IncidentIsFire)
            {
                _spread.Tick(_paused ? 0f : Time.deltaTime, _heat, _water.enabled);
            }

            if (_apparatus == null)
            {
                _apparatus = FindAnyObjectByType<ApparatusLights>();
            }

            if (_beaconPhase.Length != _beacons.Length)
            {
                _beaconPhase = new float[_beacons.Length];

                for (var i = 0; i < _beaconPhase.Length; i++)
                {
                    _beaconPhase[i] = i * 0.5f;
                }
            }

            for (var i = 0; i < _beacons.Length; i++)
            {
                var hertz = BeaconHertz * (1f + BeaconSpread * (Mathf.Repeat((i + 3) * BeaconSpreadStep, 1f) * 2f - 1f));

                _beaconPhase[i] = Mathf.Repeat(_beaconPhase[i] + hertz * Time.deltaTime, 1f);

                var lit = _apparatus != null && i < 2 ? _apparatus.BarLit(i) : _beaconPhase[i] < BeaconDuty;

                _beacons[i].intensity = _lightsEnabled && lit ? BeaconIntensity : 0f;
            }

            if (!_paused)
            {
                AdvanceRoam(Time.deltaTime);
            }

            var distance = Vector3.Distance(transform.position, _incident.position);

            // The incident text names the unit dispatched (the apparatus being driven).
            var unit = _profile.UnitName;

            var unitUpper = unit.ToUpperInvariant();

            var label = _activeIncident?.CallLabel ?? "STRUCTURE FIRE";

            var fire = IncidentIsFire;

            _status.text = _paused
                ? "RESPONSE PAUSED\nESC to resume"
                : _heat > 0f && Time.time < _alertUntil
                    ? $"NEW CALL!  {label} / {_incidentLocation}\n{DispatchLine(unit)}"
                    : _heat <= 0f && _activeIncident is null
                        ? $"{unitUpper} IN SERVICE\nWaiting for dispatch.  Free roam: drive anywhere."
                    : _heat <= 0f
                        ? $"{(fire ? "FIRE CONTAINED" : "CALL CLEARED")}\nGood work, {unit}.  Free roam: drive anywhere."
                        : _remaining <= 0f
                            ? $"RESPONSE TIME EXCEEDED\n{unit}: R to retry the call."
                            : _transporting
                                ? (distance > MonitorRange
                                    ? $"PATIENT ABOARD  /  {unitUpper} TRANSPORTING\nTake the patient to {_hospitalName}'s emergency room (marked)."
                                    : $"{unitUpper} AT {_hospitalName}\nStop at the ER bay to hand off the patient.")
                            : distance > MonitorRange
                                ? $"{label}  /  {unitUpper} RESPONDING{SeveritySuffix()}\n{_incidentLocation}.  {(fire ? "Approach the marked storefront." : "Head for the marked scene.")}"
                                : !fire
                                    ? $"{unitUpper} ON SCENE\nStop and stage here; hold still to work the call."
                                    : _profile.CanSprayWater
                                        ? $"{unitUpper} ON SCENE\nStop, then hold E to operate the water monitor."
                                        : $"{unitUpper} ON SCENE\nStage here for patient care; {unit} carries no water.";

            _telemetry.text = _heat <= 0f
                ? $"{Mathf.Abs(_speed) * 2.237f:00} MPH     FREE ROAM\n{(_activeIncident is null ? "IN SERVICE" : fire ? "FIRE OUT" : "CALL CLEARED")}     {(_liveFeed != null && _liveFeed.IsLive ? "LIVE FEED: WAITING FOR DISPATCH" : $"NEXT CALL IN {Mathf.Max(0, Mathf.CeilToInt(RoamCallDelay - _roamClock)):00}s")}"
                : $"{Mathf.Abs(_speed) * 2.237f:00} MPH     {distance:0} m TO INCIDENT\n{(fire ? "FIRE" : "SCENE")} {Mathf.CeilToInt(_heat * 100f):00}%     {Mathf.CeilToInt(_remaining):000}s REMAINING";

            if (AerialDeployed)
            {
                var telemetry = _telemetry.text.Split('\n');

                telemetry[0] = _aerial!.StatusLine;

                _telemetry.text = string.Join("\n", telemetry);
            }

            if (!_paused && NearEdge())
            {
                _status.text = "LEAVING DISPATCH AREA\nTurn back toward the city.";
            }
        }

        /// <summary>The second line of the new-call banner: whether this unit is assigned, and who else the call asks for.</summary>
        private string DispatchLine(string unit)
        {
            var upper = unit.ToUpperInvariant();

            if (_activeIncident is null || _activeIncident.Requires(PlayerIncidentUnit()))
            {
                return $"{upper} DISPATCHED{SeveritySuffix()}.  Check the map and respond.";
            }

            return $"{upper} NOT ASSIGNED (needs {_activeIncident.RequiredUnitsText()}){SeveritySuffix()}.  Respond if you can.";
        }

        /// <summary>The call's severity for the banner, e.g. "  [SEV 3]"; empty when there is no call record.</summary>
        private string SeveritySuffix()
        {
            return _activeIncident is null ? "" : $"  [SEV {_activeIncident.Severity}]";
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
            if (_heat > 0f)
            {
                return;
            }

            // A call from an outside source is dispatched soon after the last one clears; a made-up one waits out the roam delay.
            var external = _incidents.HasPending;

            if (!external && _incidentSites.Length < 2)
            {
                return;
            }

            // While the live City feed is running, calls come from it (dispatches to this unit); no made-up calls in between.
            if (!external && _liveFeed != null && _liveFeed.IsLive)
            {
                return;
            }

            _roamClock += Mathf.Max(0f, seconds);

            if (_roamClock >= (external ? ExternalCallSettleSeconds : RoamCallDelay))
            {
                StartNewCall();
            }
        }

        /// <summary>Takes the next call (from any source, else a made-up one) and starts a fresh timed response to it.</summary>
        private void StartNewCall()
        {
            _roamClock = 0f;

            var incident = TakeFallbackIncident(-1);

            if (incident is null)
            {
                return;
            }

            ShowIncident(incident);

            _transporting = false;

            _heat = 1f;

            var severityPressure = 1.3f - (0.1f * incident.Severity);

            _remaining = Mathf.Clamp((Vector3.Distance(transform.position, _incident.position) / 5f + 60f) * severityPressure, 60f, 300f);

            _alertUntil = Time.time + 6f;

            PlayDispatch();
        }

        /// <summary>The next waiting call from any source; when none waits, a made-up call (as standard JSON through the same manager). Null when there is nothing to dispatch.</summary>
        private Incident? TakeFallbackIncident(int preferredSite)
        {
            var next = _incidents.TakeNext();

            if (next is not null)
            {
                return next;
            }

            var sites = new List<Vector3>(_incidentSites.Length);

            foreach (var site in _incidentSites)
            {
                sites.Add(site.position);
            }

            var request = new FallbackRequest(
                new FallbackWorld(sites, _incidentStreets),
                PlayerIncidentUnit(),
                transform.position,
                new FallbackChoice(preferredSite, _siteIndex, MinimumCallDistance));

            _incidents.SubmitFallback(request);

            return _incidents.TakeNext();
        }

        /// <summary>The kind of unit being driven, in the terms calls use.</summary>
        private IncidentUnit PlayerIncidentUnit()
        {
            return ApparatusLoadout.Detect(transform) switch
            {
                StartingTruck.Ladder or StartingTruck.Ladder16 => IncidentUnit.Ladder,
                StartingTruck.Ambulance or StartingTruck.Medic08 or StartingTruck.Ambulance16 or StartingTruck.Supervisor16 => IncidentUnit.Ambulance,
                _ => IncidentUnit.Engine,
            };
        }

        /// <summary>The playable area as a rectangle in scene x, z, or null when none is set.</summary>
        private Rect? PlayableArea()
        {
            if (_boundsMax.x <= _boundsMin.x || _boundsMax.y <= _boundsMin.y)
            {
                return null;
            }

            return new Rect(_boundsMin, _boundsMax - _boundsMin);
        }

        /// <summary>Makes the call the active one: puts the marker (and the fire, for a fire call) where its coordinates map to.</summary>
        private void ShowIncident(Incident incident)
        {
            _activeIncident = incident;

            _outcomeReported = false;

            var nearest = NearestSite(incident.WorldPosition, out var distance);

            var atSite = nearest >= 0 && distance <= SiteSnapMeters;

            if (atSite)
            {
                MoveIncidentTo(nearest);
            }
            else
            {
                PlaceIncidentAt(incident.WorldPosition, nearest);
            }

            if (incident.Address.Length > 0)
            {
                _incidentLocation = incident.Address.ToUpperInvariant();
            }
            else if (!atSite)
            {
                _incidentLocation = incident.Geo.Latitude.ToString("0.0000") + ", " + incident.Geo.Longitude.ToString("0.0000");
            }

            if (incident.IsFire)
            {
                RestartFire();
            }
            else
            {
                _spread.Extinguish();
            }

            ShowPatient(incident, atSite);
        }

        /// <summary>EMS and medical calls get a real downed citizen at the scene (in front of the storefront when the call is at one); any other call removes the last one.</summary>
        private void ShowPatient(Incident incident, bool atSite)
        {
            if (_patient != null)
            {
                _patient.Hide(0f);
            }

            if (incident.Agency != IncidentAgency.Ems && incident.CallType != IncidentCallType.Medical)
            {
                return;
            }

            if (_patient == null)
            {
                _patient = DownedCitizen.Create();
            }

            var facing = atSite ? _incident.forward : transform.forward;

            var spot = _incident.position + (atSite ? _incident.forward * PatientFrontOffset : Vector3.zero);

            var variant = 0;

            foreach (var character in incident.Id)
            {
                variant = (variant * 31) + character;
            }

            if (!_patient.Show(new PatientRequest(spot, facing, variant, _controller)))
            {
                Debug.LogWarning("No pedestrian model is available for the patient at incident " + incident.Id + ".");
            }
        }

        /// <summary>The index of the incident site nearest the scene position (-1 with no sites) and its distance on the ground plane.</summary>
        private int NearestSite(Vector3 position, out float distance)
        {
            var best = -1;

            distance = float.MaxValue;

            for (var i = 0; i < _incidentSites.Length; i++)
            {
                var offset = _incidentSites[i].position - position;

                offset.y = 0f;

                if (offset.magnitude < distance)
                {
                    distance = offset.magnitude;

                    best = i;
                }
            }

            return best;
        }

        /// <summary>Puts the marker, fire and smoke at an exact scene position that is not at a storefront site, and hides the shop sign.</summary>
        private void PlaceIncidentAt(Vector3 world, int nearestSite)
        {
            _siteIndex = -1;

            var height = nearestSite >= 0 ? _incidentSites[nearestSite].position.y : _incident.position.y;

            var position = new Vector3(world.x, height, world.z);

            _incident.SetPositionAndRotation(position, Quaternion.identity);

            _fire.transform.position = position;

            _smoke.transform.position = position + new Vector3(0.6f, 6f, 0f);

            if (_sign != null)
            {
                _sign.gameObject.SetActive(false);
            }

            _fire.Clear();

            _smoke.Clear();
        }

        /// <summary>Tells the manager how the active call ended, once.</summary>
        private void ReportOutcome(IncidentOutcome outcome)
        {
            if (_outcomeReported || _activeIncident is null)
            {
                return;
            }

            _outcomeReported = true;

            if (outcome == IncidentOutcome.Cleared && _patient != null)
            {
                _patient.Hide(PatientClearSeconds);
            }

            _incidents.Complete(_activeIncident, outcome);
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
                _sign.gameObject.SetActive(true);

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

        /// <summary>Zooms with the mouse wheel at any time; orbits only while the right mouse button is held.</summary>
        private void UpdateCamera()
        {
            var mouse = Mouse.current;

            if (mouse is not null && Application.isFocused)
            {
                var wheel = mouse.scroll.ReadValue().y;

                if (wheel != 0f)
                {
                    ZoomCamera(Mathf.Abs(wheel) >= 10f ? wheel / WheelUnitsPerNotch : wheel);
                }
            }

            EaseZoom();

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

            ApplyCameraOffset();
        }

        /// <summary>Moves the zoom target by wheel notches (positive = scroll up = closer).</summary>
        internal void ZoomCamera(float notches)
        {
            _zoomTarget = Mathf.Clamp(_zoomTarget * Mathf.Pow(1f - ZoomPerNotch, notches), MinimumZoom, MaximumZoom);
        }

        /// <summary>Camera distance as a multiple of the authored one, after easing (1 = default framing).</summary>
        internal float CameraZoom => _zoom;

        /// <summary>Eases the camera distance towards the zoom target (unscaled time, so it also works while paused).</summary>
        private void EaseZoom()
        {
            if (Mathf.Approximately(_zoom, _zoomTarget))
            {
                return;
            }

            _zoom = Mathf.Abs(_zoom - _zoomTarget) < 0.001f ? _zoomTarget : Mathf.Lerp(_zoom, _zoomTarget, 1f - Mathf.Exp(-ZoomEase * Time.unscaledDeltaTime));

            ApplyCameraOffset();
        }

        /// <summary>Places the follow camera from the orbit angles and the current zoom: distance down to
        /// <see cref="MinimumDistanceZoom"/>, then a narrower lens for the rest of the way in.</summary>
        private void ApplyCameraOffset()
        {
            var distanceZoom = Mathf.Max(_zoom, MinimumDistanceZoom);

            _follow.FollowOffset = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f) * Vector3.back * (_initialCameraOffset.magnitude * distanceZoom);

            // Below the closest distance the field of view shrinks in proportion: zoom 0.2 = half the authored angle.
            SetFieldOfView(_defaultFieldOfView * _zoom / MinimumDistanceZoom, _zoom < MinimumDistanceZoom);
        }

        /// <summary>Applies the telephoto part of the zoom (or restores the authored field of view).</summary>
        private void SetFieldOfView(float telephoto, bool zoomedPast)
        {
            if (_followCamera == null || _defaultFieldOfView <= 0f)
            {
                return;
            }

            var lens = _followCamera.Lens;

            lens.FieldOfView = zoomedPast ? Mathf.Max(telephoto, _defaultFieldOfView * MinimumZoom / MinimumDistanceZoom) : _defaultFieldOfView;

            _followCamera.Lens = lens;
        }

        /// <summary>Restores the departure camera framing, zoom included.</summary>
        private void ResetCamera()
        {
            _orbitYaw = 0f;

            _orbitPitch = Mathf.Atan2(_initialCameraOffset.y, -_initialCameraOffset.z) * Mathf.Rad2Deg;

            _zoom = 1f;

            _zoomTarget = 1f;

            _follow.FollowOffset = _initialCameraOffset;

            SetFieldOfView(_defaultFieldOfView, false);
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

            var target = braking ? 0f : input.y * (input.y >= 0f ? TopSpeed : ReverseSpeed);

            var accelerating = !braking && Mathf.Abs(target) > Mathf.Abs(_speed) && Mathf.Sign(target) == Mathf.Sign(_speed == 0f ? target : _speed);

            _speed = Mathf.MoveTowards(_speed, target, (braking ? 22f : accelerating ? Acceleration : 9f) * Time.deltaTime);

            // The throttle opposing the direction of travel acts as a brake; the rear lights and brake sounds follow it.
            BrakeLightsOn = braking ? Mathf.Abs(_speed) > 0.2f : (input.y < -0.1f && _speed > 0.5f) || (input.y > 0.1f && _speed < -0.5f);

            ReverseEngaged = !braking && (_speed < -0.1f || (input.y < -0.1f && _speed < 0.5f));

            var steerScale = 1f / (1f + Mathf.Max(0f, Mathf.Abs(_speed) - SteerEaseSpeed) * SteerEaseRate);

            transform.Rotate(0f, input.x * _speed * _profile.TurnRate * steerScale * Time.deltaTime, 0f);

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

        /// <summary>Top forward speed and reverse speed in metres per second, how fast the engine picks up speed, and how steering eases off above <see cref="SteerEaseSpeed"/>.</summary>
        private const float TopSpeed = 22f;

        private const float ReverseSpeed = 6f;

        private const float Acceleration = 8.5f;

        private const float SteerEaseSpeed = 10f;

        private const float SteerEaseRate = 0.09f;

        /// <summary>True while the brake lights should show: braking, or the throttle held against the direction of travel.</summary>
        internal bool BrakeLightsOn { get; private set; }

        /// <summary>True while the engine is in reverse, for the white reverse lights and the backing alarm.</summary>
        internal bool ReverseEngaged { get; private set; }

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
            // From the ladder tip while the ladder is up (an aerial master stream, with its longer reach), otherwise from
            // the apparatus's monitor. Raising and extending the ladder can put the tip well over 23 m from the fire.
            var aerialStream = _aerial != null && _aerial.LadderUp;

            var origin = aerialStream ? _aerial!.TipWorld : transform.TransformPoint(_profile.WaterOrigin);

            var reach = aerialStream ? AerialStreamRange : MonitorRange;

            var offset = _incident.position - origin;

            if (!_profile.CanSprayWater || !IncidentIsFire || _paused || _remaining <= 0f || _heat <= 0f || offset.magnitude > reach || Mathf.Abs(_speed) >= 0.8f)
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

                ReportOutcome(IncidentOutcome.Cleared);
            }
        }

        /// <summary>The ER bays in the scene: every object named ER_Bay_*.</summary>
        private static Transform[] FindHospitals()
        {
            var found = new System.Collections.Generic.List<Transform>();

            foreach (var candidate in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            {
                if (candidate.name.StartsWith("ER_Bay_", System.StringComparison.Ordinal))
                {
                    found.Add(candidate);
                }
            }

            return found.ToArray();
        }

        /// <summary>True when this call has a patient, this unit is an ambulance (no water), and there is a hospital to deliver to.</summary>
        private bool CanTransportPatient()
        {
            return _hospitals.Length > 0
                && _patient != null
                && !_profile.CanSprayWater
                && _activeIncident is not null
                && (_activeIncident.Agency == IncidentAgency.Ems || _activeIncident.CallType == IncidentCallType.Medical);
        }

        /// <summary>The patient is loaded: the marker moves to the nearest hospital's ER bay and the call stays open until the unit stops there.</summary>
        private void BeginTransport()
        {
            var best = _hospitals[0];

            var bestDistance = float.MaxValue;

            foreach (var bay in _hospitals)
            {
                var offset = bay.position - transform.position;

                offset.y = 0f;

                if (offset.sqrMagnitude < bestDistance)
                {
                    bestDistance = offset.sqrMagnitude;

                    best = bay;
                }
            }

            _transporting = true;

            _hospitalName = best.name.Substring("ER_Bay_".Length).Replace('_', ' ').ToUpperInvariant();

            if (_patient != null)
            {
                _patient.Hide(0f);
            }

            PlaceIncidentAt(best.position, NearestSite(best.position, out _));

            _heat = 1f;

            _remaining = Mathf.Clamp((Mathf.Sqrt(bestDistance) / 5f) + 90f, 90f, 600f);

            _alertUntil = Time.time + 6f;
        }

        /// <summary>A call that is not a fire clears once the unit has been stopped within monitor range for a few seconds.</summary>
        private void WorkNonFireScene(float seconds)
        {
            if (IncidentIsFire || _heat <= 0f || _remaining <= 0f)
            {
                return;
            }

            if (Vector3.Distance(transform.position, _incident.position) > MonitorRange || Mathf.Abs(_speed) >= 0.8f)
            {
                return;
            }

            _heat = Mathf.Max(0f, _heat - (Mathf.Max(0f, seconds) / SceneSeconds));

            if (_heat <= 0f && !_transporting && CanTransportPatient())
            {
                BeginTransport();

                return;
            }

            if (_heat <= 0f)
            {
                PlayClear();

                ReportOutcome(IncidentOutcome.Cleared);
            }
        }

        /// <summary>Restores the dispatch without reloading other scenes.</summary>
        internal void Restart()
        {
            _controller.enabled = false;

            transform.SetPositionAndRotation(_spawn, _spawnRotation);

            _controller.enabled = true;

            _speed = 0f;

            _transporting = false;

            _heat = 1f;

            _remaining = DispatchSeconds;

            _paused = false;

            _sirenEnabled = false;

            _lightsEnabled = false;

            SceneLightsOn = false;

            _aerial?.ResetStowed();

            _leftSignal = false;

            _rightSignal = false;

            _hazards = false;

            _siren.mute = true;

            if (_horn != null)
            {
                _horn.Stop();
            }

            _airHorn?.StopNow();

            foreach (var beacon in _beacons)
            {
                beacon.intensity = 0f;
            }

            ResetCamera();

            _roamClock = 0f;

            _alertUntil = 0f;

            var retry = _activeIncident ?? _firstIncident;

            if (retry is null)
            {
                GoInService();

                return;
            }

            ShowIncident(retry);

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

