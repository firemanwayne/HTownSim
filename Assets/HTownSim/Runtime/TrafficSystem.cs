#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Ambient life for the city: cars that follow the real street grid (lanes, one-way streets, turns, signals on
    /// Elgin Street, yielding to the engine's siren) and pedestrians on the sidewalks. Agents are spawned and recycled
    /// around the player so density stays high where the player is while the rest of the city costs nothing.
    /// </summary>
    internal sealed class TrafficSystem : MonoBehaviour
    {
        private const int CarCount = 80;

        private const int PedestrianCount = 100;

        private const float SpawnMinimum = 40f;

        private const float SpawnMaximum = 220f;

        private const float DespawnDistance = 330f;

        private const float RoadEdge = 2.4f;

        private const float SidewalkInset = 1.4f;

        private const float TurnChance = 0.3f;

        private const float SignalCycle = 48f;

        private const float CarHalfLength = 2.3f;

        private const float RoadY = 0.06f;

        private const float SidewalkY = 0.2f;

        /// <summary>Siren yielding: cars within YieldRange slow and pull over, stop inside StopRange, hold for YieldHoldSeconds after the engine passes, and wait before intersections the engine is closing on.</summary>
        private const float YieldRange = 90f;

        private const float StopRange = 60f;

        private const float YieldNearRange = 35f;

        private const float YieldHoldSeconds = 3f;

        private const float CreepSpeed = 3f;

        private const float IntersectionClearRange = 80f;

        private const float MinimumPull = 0.6f;

        private const float MaximumPull = 3.2f;

        private const float CurbClearance = 1.2f;

        /// <summary>Collision tuning: how hard a struck car is thrown relative to the engine's speed, its mass, how long it is left wrecked, and how long pedestrians stay down.</summary>
        private const float CarKnockFactor = 1.15f;

        private const float CarKnockSpeedLimit = 20f;

        private const float CarMass = 1400f;

        private const float WreckSeconds = 20f;

        private const float WreckRestSeconds = 6f;

        private const float IgnoreEngineSeconds = 0.6f;

        private const float PedestrianMass = 75f;

        private const float PedestrianDownSeconds = 6f;

        private const float PedestrianHitSpeed = 1f;

        private const float EngineHalfWidth = 1.9f;

        private const float EngineRear = -4.5f;

        private const float EngineFront = 4.6f;

        /// <summary>Things the scene builder wires up.</summary>
        [SerializeField] private Transform _player = null!;

        [SerializeField] private FireResponse _response = null!;

        [SerializeField] private GameObject _carTemplate = null!;

        [SerializeField] private GameObject _pedestrianTemplate = null!;

        [SerializeField] private Material[] _carPaints = new Material[0];

        /// <summary>Optional civilian car models; when set, each traffic car wears one instead of the primitive body.</summary>
        [SerializeField] private GameObject[] _carModels = new GameObject[0];

        private const float ModelLength = 4.4f;

        /// <summary>Ground speed in metres per second that the walk clips were authored for; playback is scaled by speed over this.</summary>
        private const float WalkClipSpeed = 1.3f;

        private const string PaletteMaterialPrefix = "PixelColors";
        private const string SeparateWheelPrefix = "Wheel_";
        private static readonly string[] WheelNames = { "FL", "FR", "BL", "BR" };
        private static readonly Color[] PaintTints =
        {
            new Color(1f, 1f, 1f), new Color(1f, 0.55f, 0.5f), new Color(0.6f, 0.75f, 1f), new Color(0.65f, 0.95f, 0.7f),
            new Color(1f, 0.88f, 0.5f), new Color(0.78f, 0.78f, 0.82f), new Color(0.45f, 0.45f, 0.5f), new Color(0.85f, 0.65f, 1f),
        };
        private Material? _brakeMaterial;

        /// <summary>Optional humanoid pedestrian models; when set, each pedestrian wears one and plays a walk clip.</summary>
        [SerializeField] private GameObject[] _pedestrianModels = new GameObject[0];

        /// <summary>The pedestrian models, which <see cref="DownedCitizen"/> also uses for EMS patients.</summary>
        internal GameObject[] PedestrianModels => _pedestrianModels;

        [SerializeField] private AnimationClip[] _pedestrianWalkClips = new AnimationClip[0];

        [SerializeField] private Material[] _shirtPaints = new Material[0];

        [SerializeField] private Material[] _skinPaints = new Material[0];

        /// <summary>Street grid: centre and paved-plus-sidewalk width of every north-south (along Z) and east-west (along X) street.</summary>
        [SerializeField] private float[] _nsCentre = new float[0];

        [SerializeField] private float[] _nsWidth = new float[0];

        [SerializeField] private bool[] _nsOneWay = new bool[0];

        [SerializeField] private float[] _ewCentre = new float[0];

        [SerializeField] private float[] _ewWidth = new float[0];

        [SerializeField] private bool[] _ewOneWay = new bool[0];

        /// <summary>Optional first and last coordinate along each street where it is paved and continuous; empty means the whole bounds.</summary>
        [SerializeField] private float[] _nsMin = new float[0];

        [SerializeField] private float[] _nsMax = new float[0];

        [SerializeField] private float[] _ewMin = new float[0];

        [SerializeField] private float[] _ewMax = new float[0];

        /// <summary>Yaw in degrees of the street grid's own frame relative to the world axes. Street centres and extents are in that frame.</summary>
        [SerializeField] private float _frameYawDegrees;

        [SerializeField] private Vector2 _boundsMin;

        [SerializeField] private Vector2 _boundsMax;

        /// <summary>Z of the signalised east-west street (Elgin Street).</summary>
        [SerializeField] private float _signalZ;

        [SerializeField] private Renderer[] _ewRed = new Renderer[0];

        [SerializeField] private Renderer[] _ewAmber = new Renderer[0];

        [SerializeField] private Renderer[] _ewGreen = new Renderer[0];

        [SerializeField] private Renderer[] _nsRed = new Renderer[0];

        [SerializeField] private Renderer[] _nsAmber = new Renderer[0];

        [SerializeField] private Renderer[] _nsGreen = new Renderer[0];

        [SerializeField] private Material? _redOn;

        [SerializeField] private Material? _redOff;

        [SerializeField] private Material? _amberOn;

        [SerializeField] private Material? _amberOff;

        [SerializeField] private Material? _greenOn;

        [SerializeField] private Material? _greenOff;

        private readonly List<Axis> _axes = new List<Axis>();

        private readonly List<Car> _cars = new List<Car>();

        private readonly List<Pedestrian> _pedestrians = new List<Pedestrian>();

        private readonly Dictionary<Collider, Car> _carByCollider = new Dictionary<Collider, Car>();

        private CharacterController _playerController = null!;

        private AudioClip _impactClip = null!;

        private float _clock;

        private SignalState _lastEw = (SignalState)(-1);

        private SignalState _lastNs = (SignalState)(-1);

        /// <summary>Traffic light colour.</summary>
        private enum SignalState
        {
            Green,
            Amber,
            Red,
        }

        /// <summary>One street of the grid.</summary>
        private sealed class Axis
        {
            public bool AlongZ;

            public float Fixed;

            public float HalfWidth;

            public bool OneWay;

            public float Min;

            public float Max;

            /// <summary>Streets that cross this one, sorted by their position along it.</summary>
            public readonly List<Axis> Crossings = new List<Axis>();
        }

        /// <summary>A civilian car near a point, as seen by the sound system.</summary>
        internal struct NearCar
        {
            public int Id;

            public Vector3 Position;

            public float Speed;
        }

        private sealed class Car
        {
            public Transform Transform = null!;

            public Renderer? Body;

            /// <summary>Wheel pivots of the attached model, rolled by distance travelled.</summary>
            public Transform[] Wheels = new Transform[0];

            public float WheelRadius = 0.33f;

            /// <summary>Per-car copies of the palette material, tinted for paint variety.</summary>
            public Material[] Paint = new Material[0];

            public Renderer[] BrakeLights = new Renderer[0];

            public bool BrakeOn;

            public Axis Axis = null!;

            public int Direction;

            public float Along;

            public float Speed;

            public float Cruise;

            public float Pull;

            /// <summary>Half the vehicle's length, used for following gaps and stop lines; longer for buses and trucks.</summary>
            public float HalfLength = CarHalfLength;

            /// <summary>+1 drives in the right-hand lane, -1 in the left-hand lane; only one-way streets (dashed centre line, two lanes) use -1.</summary>
            public float LaneSide = 1f;

            public float TurnCooldown;

            public int Id;

            public bool Active;

            public Rigidbody Rb = null!;

            public Collider Collider = null!;

            /// <summary>True while the car has left its path and is a free physics body after being struck.</summary>
            public bool Knocked;

            public float KnockTime;

            public float Rest;

            public float IgnoreTimer;

            public float YieldHold;
        }

        private sealed class Pedestrian
        {
            public Transform Transform = null!;

            public Transform LegLeft = null!;

            public Transform LegRight = null!;

            public Transform ArmLeft = null!;

            public Transform ArmRight = null!;

            public Axis Axis = null!;

            public float Side;

            public float Along;

            public int Direction;

            public float Speed;

            public float Phase;

            public float Min;

            public float Max;

            public bool Active;

            /// <summary>True when a humanoid model with a walk clip replaces the primitive body.</summary>
            public bool Rigged;

            public Animator? Animator;

            public PlayableGraph Graph;

            public AnimationClipPlayable Clip;

            /// <summary>True while the pedestrian is a tumbling physics body after being struck.</summary>
            public bool Knocked;

            public float KnockTime;
        }

        /// <summary>Builds the street model and the agent pools.</summary>
        private void Start()
        {
            _frame = Quaternion.Euler(0f, _frameYawDegrees, 0f);

            _frameInverse = Quaternion.Inverse(_frame);

            BuildAxes(_nsCentre, _nsWidth, _nsOneWay, _nsMin, _nsMax, true);

            BuildAxes(_ewCentre, _ewWidth, _ewOneWay, _ewMin, _ewMax, false);

            foreach (var axis in _axes)
            {
                foreach (var other in _axes)
                {
                    if (other.AlongZ != axis.AlongZ)
                    {
                        axis.Crossings.Add(other);
                    }
                }

                axis.Crossings.Sort((a, b) => a.Fixed.CompareTo(b.Fixed));
            }

            _playerController = _player.GetComponent<CharacterController>();

            gameObject.AddComponent<CityAmbience>().Initialize(this, _response);

            _response.Traffic = this;

            _impactClip = CreateImpactClip();

            _carTemplate.SetActive(false);

            _pedestrianTemplate.SetActive(false);

            for (var i = 0; i < CarCount; i++)
            {
                var go = Instantiate(_carTemplate, transform);

                go.SetActive(false);

                var bodyRenderer = go.transform.Find("Body").GetComponent<Renderer>();

                if (_carModels.Length > 0)
                {
                    bodyRenderer = null;
                }

                var car = new Car { Transform = go.transform, Body = bodyRenderer, Id = i, Rb = go.GetComponent<Rigidbody>(), Collider = go.GetComponent<Collider>() };

                if (_carModels.Length > 0)
                {
                    AttachModel(car, _carModels[i % _carModels.Length]);
                }

                _cars.Add(car);

                _carByCollider[car.Collider] = car;
            }

            for (var i = 0; i < PedestrianCount; i++)
            {
                var go = Instantiate(_pedestrianTemplate, transform);

                go.SetActive(false);

                var t = go.transform;

                var pedestrian = new Pedestrian
                {
                    Transform = t,
                    LegLeft = t.Find("LegL"),
                    LegRight = t.Find("LegR"),
                    ArmLeft = t.Find("ArmL"),
                    ArmRight = t.Find("ArmR"),
                };

                if (_pedestrianModels.Length > 0 && _pedestrianWalkClips.Length > 0)
                {
                    AttachPedestrianModel(pedestrian, i);
                }

                _pedestrians.Add(pedestrian);
            }

            ApplySignals(force: true);

            foreach (var car in _cars)
            {
                TrySpawnCar(car, true);
            }

            foreach (var ped in _pedestrians)
            {
                TrySpawnPedestrian(ped);
            }
        }

        private void BuildAxes(float[] centres, float[] widths, bool[] oneWay, float[] mins, float[] maxs, bool alongZ)
        {
            for (var i = 0; i < centres.Length; i++)
            {
                _axes.Add(new Axis
                {
                    AlongZ = alongZ,
                    Fixed = centres[i],
                    HalfWidth = widths[i] / 2f,
                    OneWay = i < oneWay.Length && oneWay[i],
                    Min = i < mins.Length ? mins[i] : alongZ ? _boundsMin.y + 20f : _boundsMin.x + 20f,
                    Max = i < maxs.Length ? maxs[i] : alongZ ? _boundsMax.y - 20f : _boundsMax.x - 20f,
                });
            }
        }

        /// <summary>Advances signals, cars and pedestrians.</summary>
        private void Update()
        {
            if (_response.IsPaused)
            {
                return;
            }

            var dt = Mathf.Min(Time.deltaTime, 0.05f);

            _clock += dt;

            ApplySignals(force: false);

            var respawned = 0;

            foreach (var car in _cars)
            {
                if (car.Active)
                {
                    StepCar(car, dt);
                }
                else if (respawned < 2 && TrySpawnCar(car, false))
                {
                    respawned++;
                }
            }

            foreach (var ped in _pedestrians)
            {
                if (ped.Active)
                {
                    StepPedestrian(ped, dt);
                }
                else if (respawned < 4 && TrySpawnPedestrian(ped))
                {
                    respawned++;
                }
            }
        }

        // ------------------------------------------------------------------ signals

        private SignalState StateFor(bool eastWest)
        {
            var t = _clock % SignalCycle;

            if (eastWest)
            {
                return t < 18f ? SignalState.Green : t < 21f ? SignalState.Amber : SignalState.Red;
            }

            return t < 24f ? SignalState.Red : t < 42f ? SignalState.Green : t < 45f ? SignalState.Amber : SignalState.Red;
        }

        private void ApplySignals(bool force)
        {
            var ew = StateFor(true);

            var ns = StateFor(false);

            if (force || ew != _lastEw)
            {
                SetLenses(_ewRed, _ewAmber, _ewGreen, ew);

                _lastEw = ew;
            }

            if (force || ns != _lastNs)
            {
                SetLenses(_nsRed, _nsAmber, _nsGreen, ns);

                _lastNs = ns;
            }
        }

        private void SetLenses(Renderer[] red, Renderer[] amber, Renderer[] green, SignalState state)
        {
            Apply(red, state == SignalState.Red ? _redOn : _redOff);

            Apply(amber, state == SignalState.Amber ? _amberOn : _amberOff);

            Apply(green, state == SignalState.Green ? _greenOn : _greenOff);
        }

        private static void Apply(Renderer[] renderers, Material? material)
        {
            if (material is null)
            {
                return;
            }

            foreach (var renderer in renderers)
            {
                renderer.sharedMaterial = material;
            }
        }

        private bool IsSignalised(Axis axis, Axis crossing)
        {
            var elgin = axis.AlongZ ? crossing : axis;

            return !elgin.AlongZ && Mathf.Abs(elgin.Fixed - _signalZ) < 0.5f;
        }

        // ------------------------------------------------------------------ geometry helpers

        private static float Lane(Axis axis)
        {
            return Mathf.Max(1.5f, (axis.HalfWidth - RoadEdge) / 2f);
        }

        /// <summary>Right-hand traffic: +1 means the +X (or +Z) side is on the driver's right.</summary>
        private static float RightSign(Axis axis, int direction)
        {
            return axis.AlongZ ? direction : -direction;
        }

        /// <summary>Rotation from the street grid's frame to the world, and back (identity when the grid is aligned with the world axes).</summary>
        private static Quaternion _frame = Quaternion.identity;

        private static Quaternion _frameInverse = Quaternion.identity;

        private static Vector3 Heading(Axis axis, int direction)
        {
            return _frame * (axis.AlongZ ? new Vector3(0f, 0f, direction) : new Vector3(direction, 0f, 0f));
        }

        private static Vector3 PointOn(Axis axis, float across, float along, float y)
        {
            var point = _frame * (axis.AlongZ ? new Vector3(across, 0f, along) : new Vector3(along, 0f, across));

            point.y = y;

            return point;
        }

        private static float AlongOf(Axis axis, Vector3 point)
        {
            var local = _frameInverse * new Vector3(point.x, 0f, point.z);

            return axis.AlongZ ? local.z : local.x;
        }

        private static float AcrossOf(Axis axis, Vector3 point)
        {
            var local = _frameInverse * new Vector3(point.x, 0f, point.z);

            return axis.AlongZ ? local.x : local.z;
        }

        private static bool NearCrossing(Axis axis, float along, float margin)
        {
            foreach (var crossing in axis.Crossings)
            {
                if (Mathf.Abs(along - crossing.Fixed) < crossing.HalfWidth + margin)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ cars

        private bool TrySpawnCar(Car car, bool initial)
        {
            for (var attempt = 0; attempt < 14; attempt++)
            {
                var axis = _axes[Random.Range(0, _axes.Count)];

                var direction = axis.OneWay ? 1 : Random.value < 0.5f ? -1 : 1;

                var along = Random.Range(axis.Min, axis.Max);

                if (NearCrossing(axis, along, 14f))
                {
                    continue;
                }

                var laneSide = axis.OneWay && Random.value < 0.5f ? -1f : 1f;

                var point = PointOn(axis, axis.Fixed + RightSign(axis, direction) * laneSide * Lane(axis), along, RoadY);

                var distance = Vector3.Distance(point, _player.position);

                if (distance < SpawnMinimum || distance > SpawnMaximum || CarNear(point, 12f))
                {
                    continue;
                }

                car.Axis = axis;

                car.LaneSide = laneSide;

                car.Direction = direction;

                car.Along = along;

                car.Speed = 0f;

                car.Cruise = Random.Range(6f, 11f);

                car.Pull = 0f;

                car.TurnCooldown = 1f;

                car.Active = true;

                if (car.Body is not null)
                {
                    car.Body.sharedMaterial = _carPaints[Random.Range(0, _carPaints.Length)];
                }

                var tint = PaintTints[Random.Range(0, PaintTints.Length)];

                foreach (var paint in car.Paint)
                {
                    paint.SetColor("_BaseColor", tint);
                }

                var scale = car.Body is null
                    ? Vector3.one * Random.Range(0.97f, 1.04f)
                    : new Vector3(1f, Random.Range(0.95f, 1.25f), Random.Range(0.92f, 1.2f));

                car.Transform.localScale = scale;

                car.Transform.SetPositionAndRotation(point, Quaternion.LookRotation(Heading(axis, direction)));

                car.Transform.gameObject.SetActive(true);

                return true;
            }

            return false;
        }

        /// <summary>Hides the primitive car parts, parents a scaled and grounded copy of a model under the car, and finds its wheels, paint and brake lights.</summary>
        private void AttachModel(Car car, GameObject model)
        {
            var root = car.Transform;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }

            var instance = Instantiate(model, root);

            instance.name = "Model";

            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            {
                Destroy(collider);
            }

            instance.transform.localPosition = Vector3.zero;

            instance.transform.localRotation = Quaternion.identity;

            instance.transform.localScale = Vector3.one;

            var bounds = LocalBounds(instance.transform, root);

            if (bounds.size.x > bounds.size.z)
            {
                instance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

                bounds = LocalBounds(instance.transform, root);
            }

            var length = LengthFor(model.name);

            var scale = length / Mathf.Max(bounds.size.z, 0.01f);

            instance.transform.localScale = Vector3.one * scale;

            bounds = LocalBounds(instance.transform, root);

            instance.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

            bounds = LocalBounds(instance.transform, root);

            car.HalfLength = length / 2f;

            if (car.Collider is BoxCollider box)
            {
                box.size = new Vector3(bounds.size.x + 0.1f, bounds.size.y, bounds.size.z + 0.1f);

                box.center = new Vector3(0f, bounds.size.y / 2f, 0f);
            }

            var wheels = new List<Transform>();

            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
            {
                if (System.Array.IndexOf(WheelNames, child.name) >= 0 || child.name.StartsWith(SeparateWheelPrefix))
                {
                    wheels.Add(child);
                }
            }

            car.Wheels = wheels.ToArray();

            if (wheels.Count > 0)
            {
                var wheelBounds = LocalBounds(wheels[0], root);

                car.WheelRadius = Mathf.Max(0.15f, wheelBounds.size.y / 2f);
            }

            var paint = new List<Material>();

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;

                for (var m = 0; m < materials.Length; m++)
                {
                    if (materials[m] != null && materials[m].name.StartsWith(PaletteMaterialPrefix))
                    {
                        materials[m] = new Material(materials[m]);

                        paint.Add(materials[m]);
                    }
                }

                renderer.sharedMaterials = materials;
            }

            car.Paint = paint.ToArray();

            car.BrakeLights = BuildBrakeLights(root, bounds);
        }

        /// <summary>Real-world length in metres for a vehicle model, picked from its name; unknown models use the default car length.</summary>
        private static float LengthFor(string modelName)
        {
            if (modelName.Contains("Bus"))
            {
                return 8f;
            }

            if (modelName.Contains("Container"))
            {
                return 7.2f;
            }

            if (modelName.Contains("Truck_color") && !modelName.Contains("Pick"))
            {
                return 6.8f;
            }

            if (modelName.Contains("Ambulance"))
            {
                return 5.6f;
            }

            if (modelName.Contains("Pick"))
            {
                return 5.2f;
            }

            if (modelName.Contains("SUV"))
            {
                return 4.9f;
            }

            if (modelName.Contains("MicroBus"))
            {
                return 5f;
            }

            return modelName.Contains("Vehicle_") ? 4.5f : ModelLength;
        }

        /// <summary>Two small emissive red lamps across the rear of the car, shown only while braking.</summary>
        private Renderer[] BuildBrakeLights(Transform root, Bounds bounds)
        {
            if (_brakeMaterial == null)
            {
                _brakeMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "BrakeLight" };

                _brakeMaterial.SetColor("_BaseColor", new Color(0.6f, 0f, 0f));

                _brakeMaterial.EnableKeyword("_EMISSION");

                _brakeMaterial.SetColor("_EmissionColor", new Color(3.5f, 0.1f, 0.05f));
            }

            var lights = new Renderer[2];

            for (var i = 0; i < 2; i++)
            {
                var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);

                Destroy(lamp.GetComponent<Collider>());

                lamp.name = "BrakeLight";

                lamp.transform.SetParent(root, false);

                var side = i == 0 ? -1f : 1f;

                lamp.transform.localPosition = new Vector3(bounds.center.x + side * bounds.extents.x * 0.62f, bounds.min.y + bounds.size.y * 0.42f, bounds.min.z - 0.01f);

                lamp.transform.localScale = new Vector3(0.3f, 0.12f, 0.05f);

                var renderer = lamp.GetComponent<Renderer>();

                renderer.sharedMaterial = _brakeMaterial;

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                renderer.enabled = false;

                lights[i] = renderer;
            }

            return lights;
        }

        private static Bounds LocalBounds(Transform model, Transform space)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);

            var first = true;

            foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh is null)
                {
                    continue;
                }

                var box = filter.sharedMesh.bounds;

                for (var i = 0; i < 8; i++)
                {
                    var corner = box.center + Vector3.Scale(box.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));

                    var point = space.InverseTransformPoint(filter.transform.TransformPoint(corner));

                    if (first)
                    {
                        bounds = new Bounds(point, Vector3.zero);

                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return bounds;
        }

        private bool CarNear(Vector3 point, float radius)
        {
            foreach (var other in _cars)
            {
                if (other.Active && (other.Transform.position - point).sqrMagnitude < radius * radius)
                {
                    return true;
                }
            }

            return false;
        }

        private void StepCar(Car car, float dt)
        {
            if (car.Knocked)
            {
                StepKnocked(car, dt);

                return;
            }

            var axis = car.Axis;

            var heading = Heading(axis, car.Direction);

            var position = car.Transform.position;

            var toPlayer = _player.position - position;

            toPlayer.y = 0f;

            var playerDistance = toPlayer.magnitude;

            if (playerDistance > DespawnDistance || car.Along < axis.Min - 10f || car.Along > axis.Max + 10f)
            {
                Despawn(car);

                return;
            }

            var free = float.MaxValue;

            var yieldNow = _response.SirenActive && playerDistance < YieldRange && (Vector3.Dot(toPlayer, heading) < 0f || playerDistance < YieldNearRange);

            car.YieldHold = yieldNow ? YieldHoldSeconds : Mathf.Max(0f, car.YieldHold - dt);

            var yielding = yieldNow || car.YieldHold > 0f;

            car.Pull = Mathf.MoveTowards(car.Pull, yielding ? PullDistance(axis) + (car.LaneSide < 0f ? 2f * Lane(axis) : 0f) : 0f, 3f * dt);

            if (yielding && (playerDistance < StopRange || !yieldNow))
            {
                free = 0f;
            }

            free = Mathf.Min(free, GapToVehicleAhead(car, heading, position));

            free = Mathf.Min(free, GapToIntersectionObstacle(car));

            var target = free < 5f ? 0f : Mathf.Min(car.Cruise, free * 1.1f);

            if (yielding)
            {
                target = Mathf.Min(target, CreepSpeed);
            }

            var braking = target < car.Speed - 0.05f || target < 0.5f;

            car.Speed = Mathf.MoveTowards(car.Speed, target, (target < car.Speed ? 11f : 3.2f) * dt);

            var previous = car.Along;

            car.Along += car.Direction * car.Speed * dt;

            car.TurnCooldown -= dt;

            if (car.TurnCooldown <= 0f)
            {
                TryTurn(car, previous);
            }

            var lateral = car.LaneSide * Lane(car.Axis) + car.Pull;

            var across = car.Axis.Fixed + RightSign(car.Axis, car.Direction) * lateral;

            var targetPosition = PointOn(car.Axis, across, car.Along, RoadY);

            var targetRotation = Quaternion.LookRotation(Heading(car.Axis, car.Direction));

            var blend = 1f - Mathf.Exp(-9f * dt);

            car.Transform.SetPositionAndRotation(Vector3.Lerp(position, targetPosition, blend), Quaternion.Slerp(car.Transform.rotation, targetRotation, blend));

            AnimateCar(car, braking, dt);
        }

        /// <summary>Rolls the wheels by distance travelled and lights the brake lights while the car is slowing or stopped.</summary>
        private static void AnimateCar(Car car, bool braking, float dt)
        {
            if (car.Wheels.Length > 0)
            {
                var degrees = car.Speed * dt / car.WheelRadius * Mathf.Rad2Deg;

                var axle = car.Transform.right;

                foreach (var wheel in car.Wheels)
                {
                    wheel.Rotate(axle, degrees, Space.World);
                }
            }

            if (braking == car.BrakeOn)
            {
                return;
            }

            car.BrakeOn = braking;

            foreach (var light in car.BrakeLights)
            {
                light.enabled = braking;
            }
        }

        private void Despawn(Car car)
        {
            if (car.Knocked)
            {
                car.Knocked = false;

                Physics.IgnoreCollision(car.Collider, _playerController, false);

                car.Rb.linearVelocity = Vector3.zero;

                car.Rb.angularVelocity = Vector3.zero;

                car.Rb.isKinematic = true;

                car.Rb.constraints = RigidbodyConstraints.None;

                car.Rb.interpolation = RigidbodyInterpolation.None;
            }

            car.Active = false;

            car.Transform.gameObject.SetActive(false);
        }

        /// <summary>Distance to the nearest car or the player in this car's lane ahead, or a large number.</summary>
        private float GapToVehicleAhead(Car car, Vector3 heading, Vector3 position)
        {
            var best = float.MaxValue;

            foreach (var other in _cars)
            {
                if (!other.Active || other == car)
                {
                    continue;
                }

                if (other.Knocked)
                {
                    best = Mathf.Min(best, GapToBody(other.Transform.position - position, heading, car.HalfLength));

                    continue;
                }

                if (other.Axis != car.Axis || other.Direction != car.Direction)
                {
                    continue;
                }

                var gap = (other.Along - car.Along) * car.Direction - (car.HalfLength + other.HalfLength);

                if (gap > -1f && gap < best)
                {
                    best = gap;
                }
            }

            return Mathf.Min(best, GapToBody(_player.position - position, heading, car.HalfLength));
        }

        /// <summary>Distance to a body in this car's lane ahead, given the vector to it, or a large number when it is behind or off to the side.</summary>
        private static float GapToBody(Vector3 toBody, Vector3 heading, float halfLength)
        {
            var ahead = Vector3.Dot(toBody, heading) - halfLength - 2.2f;

            var lateral = Mathf.Abs(Vector3.Dot(toBody, Vector3.Cross(Vector3.up, heading)));

            return ahead > -1f && lateral < 2.8f ? ahead : float.MaxValue;
        }

        /// <summary>How far a yielding car pulls toward the curb: the free room between its lane and the kerb, within sensible limits.</summary>
        private static float PullDistance(Axis axis)
        {
            return Mathf.Clamp(axis.HalfWidth - RoadEdge - CurbClearance - Lane(axis), MinimumPull, MaximumPull);
        }

        /// <summary>True while the engine, with its siren on, is close enough to an intersection that cross and turning traffic should hold back.</summary>
        private bool EngineClosingOn(Axis axis, Axis crossing)
        {
            if (!_response.SirenActive)
            {
                return false;
            }

            var centre = PointOn(axis, axis.Fixed, crossing.Fixed, _player.position.y);

            return Vector3.Distance(centre, _player.position) < IntersectionClearRange;
        }

        /// <summary>Applies signals and cross-traffic yielding at the next intersection ahead.</summary>
        private float GapToIntersectionObstacle(Car car)
        {
            var best = float.MaxValue;

            foreach (var crossing in car.Axis.Crossings)
            {
                var stopLine = crossing.Fixed - car.Direction * (crossing.HalfWidth + 1.6f);

                var distance = (stopLine - car.Along) * car.Direction - car.HalfLength;

                if (distance < -1f || distance > 45f)
                {
                    continue;
                }

                if (distance < 30f && distance < best && EngineClosingOn(car.Axis, crossing))
                {
                    best = distance;
                }

                if (IsSignalised(car.Axis, crossing))
                {
                    var state = StateFor(!car.Axis.AlongZ);

                    var mustStop = state == SignalState.Red || (state == SignalState.Amber && distance > car.Speed * car.Speed / 7f);

                    if (mustStop && distance < best)
                    {
                        best = distance;
                    }

                    continue;
                }

                if (distance < 20f && CrossTrafficHasPriority(car, crossing) && distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        private bool CrossTrafficHasPriority(Car car, Axis crossing)
        {
            var myDistance = Mathf.Abs(crossing.Fixed - car.Along);

            foreach (var other in _cars)
            {
                if (!other.Active || other.Axis != crossing)
                {
                    continue;
                }

                var theirDistance = Mathf.Abs(other.Along - car.Axis.Fixed);

                var theirsFirst = theirDistance < myDistance || (Mathf.Approximately(theirDistance, myDistance) && other.Id < car.Id);

                if (theirDistance < 14f && (theirsFirst || theirDistance < 6f))
                {
                    return true;
                }
            }

            return false;
        }

        private void TryTurn(Car car, float previous)
        {
            foreach (var crossing in car.Axis.Crossings)
            {
                var crossed = (previous < crossing.Fixed) != (car.Along < crossing.Fixed);

                if (!crossed)
                {
                    continue;
                }

                if (Random.value < TurnChance && car.Axis.Fixed > crossing.Min && car.Axis.Fixed < crossing.Max)
                {
                    var oldAxis = car.Axis;

                    car.Axis = crossing;

                    car.LaneSide = crossing.OneWay && Random.value < 0.5f ? -1f : 1f;

                    car.Direction = crossing.OneWay ? 1 : Random.value < 0.5f ? -1 : 1;

                    car.Along = oldAxis.Fixed;

                    car.TurnCooldown = 2f;
                }

                return;
            }
        }

        // ------------------------------------------------------------------ pedestrians

        private bool TrySpawnPedestrian(Pedestrian ped)
        {
            for (var attempt = 0; attempt < 14; attempt++)
            {
                var axis = _axes[Random.Range(0, _axes.Count)];

                var along = Random.Range(axis.Min, axis.Max);

                if (NearCrossing(axis, along, 1.5f))
                {
                    continue;
                }

                var side = Random.value < 0.5f ? -1f : 1f;

                var point = PointOn(axis, axis.Fixed + side * (axis.HalfWidth - SidewalkInset), along, SidewalkY);

                var distance = Vector3.Distance(point, _player.position);

                if (distance < 25f || distance > SpawnMaximum - 40f)
                {
                    continue;
                }

                ped.Axis = axis;

                ped.Side = side;

                ped.Along = along;

                ped.Direction = Random.value < 0.5f ? -1 : 1;

                ped.Speed = Random.Range(1.1f, 1.7f);

                ped.Phase = Random.value * 6.28f;

                SegmentBounds(axis, along, out ped.Min, out ped.Max);

                Dress(ped);

                ped.Active = true;

                if (ped.Rigged)
                {
                    ped.Clip.SetTime(Random.value * ped.Clip.GetAnimationClip().length);
                }

                ped.Transform.position = point;

                ped.Transform.gameObject.SetActive(true);

                return true;
            }

            return false;
        }

        private void Dress(Pedestrian ped)
        {
            if (_shirtPaints.Length > 0)
            {
                var torso = ped.Transform.Find("Torso");

                if (torso != null)
                {
                    torso.GetComponent<Renderer>().sharedMaterial = _shirtPaints[Random.Range(0, _shirtPaints.Length)];
                }
            }

            if (_skinPaints.Length > 0)
            {
                var skin = _skinPaints[Random.Range(0, _skinPaints.Length)];

                foreach (var name in new[] { "Head" })
                {
                    var part = ped.Transform.Find(name);

                    if (part != null)
                    {
                        part.GetComponent<Renderer>().sharedMaterial = skin;
                    }
                }

                foreach (var arm in new[] { ped.ArmLeft, ped.ArmRight })
                {
                    arm.GetComponentInChildren<Renderer>().sharedMaterial = skin;
                }
            }
        }

        /// <summary>Limits a walker to the stretch of sidewalk between two intersections.</summary>
        private static void SegmentBounds(Axis axis, float along, out float min, out float max)
        {
            min = axis.Min;

            max = axis.Max;

            foreach (var crossing in axis.Crossings)
            {
                var low = crossing.Fixed - crossing.HalfWidth - 0.3f;

                var high = crossing.Fixed + crossing.HalfWidth + 0.3f;

                if (high <= along)
                {
                    min = Mathf.Max(min, high);
                }
                else if (low >= along)
                {
                    max = Mathf.Min(max, low);

                    break;
                }
            }
        }

        private void StepPedestrian(Pedestrian ped, float dt)
        {
            var position = ped.Transform.position;

            var toPlayer = _player.position - position;

            toPlayer.y = 0f;

            var distance = toPlayer.magnitude;

            if (ped.Knocked)
            {
                StepKnockedPedestrian(ped, dt, distance);

                return;
            }

            if (distance > DespawnDistance)
            {
                ped.Active = false;

                ped.Transform.gameObject.SetActive(false);

                return;
            }

            if (Mathf.Abs(_response.SignedSpeed) > PedestrianHitSpeed && InEngineFootprint(position))
            {
                KnockPedestrian(ped, _player.forward * _response.SignedSpeed);

                return;
            }

            var speed = ped.Speed;

            if (distance < 6f)
            {
                var away = AlongOf(ped.Axis, position) - AlongOf(ped.Axis, _player.position);

                ped.Direction = away >= 0f ? 1 : -1;

                speed = 3.6f;
            }

            ped.Along += ped.Direction * speed * dt;

            if (ped.Along < ped.Min || ped.Along > ped.Max)
            {
                ped.Along = Mathf.Clamp(ped.Along, ped.Min, ped.Max);

                ped.Direction = -ped.Direction;
            }

            ped.Phase += speed * dt * 4.2f;

            var bob = 0f;

            if (ped.Rigged)
            {
                AnimatePedestrian(ped, speed);
            }
            else
            {
                var swing = Mathf.Sin(ped.Phase) * 32f;

                ped.LegLeft.localRotation = Quaternion.Euler(swing, 0f, 0f);

                ped.LegRight.localRotation = Quaternion.Euler(-swing, 0f, 0f);

                ped.ArmLeft.localRotation = Quaternion.Euler(-swing * 0.8f, 0f, 0f);

                ped.ArmRight.localRotation = Quaternion.Euler(swing * 0.8f, 0f, 0f);

                bob = Mathf.Abs(Mathf.Sin(ped.Phase)) * 0.04f;
            }

            var point = PointOn(ped.Axis, ped.Axis.Fixed + ped.Side * (ped.Axis.HalfWidth - SidewalkInset), ped.Along, SidewalkY + bob);

            ped.Transform.SetPositionAndRotation(point, Quaternion.LookRotation(Heading(ped.Axis, ped.Direction)));
        }

        /// <summary>Hides the primitive body and parents a humanoid model driven by a looping walk clip through a playable graph.</summary>
        private void AttachPedestrianModel(Pedestrian ped, int index)
        {
            foreach (var renderer in ped.Transform.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }

            var instance = Instantiate(_pedestrianModels[index % _pedestrianModels.Length], ped.Transform);

            instance.name = "Model";

            instance.transform.localPosition = Vector3.zero;

            instance.transform.localRotation = Quaternion.identity;

            foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                DestroyImmediate(behaviour);
            }

            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            {
                DestroyImmediate(collider);
            }

            var animator = instance.GetComponentInChildren<Animator>();

            if (animator == null || animator.avatar == null)
            {
                Destroy(instance);

                foreach (var renderer in ped.Transform.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = true;
                }

                return;
            }

            ped.Animator = animator;

            animator.runtimeAnimatorController = null;

            animator.applyRootMotion = false;

            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            ped.Graph = PlayableGraph.Create("Pedestrian" + index);

            var output = AnimationPlayableOutput.Create(ped.Graph, "Walk", animator);

            ped.Clip = AnimationClipPlayable.Create(ped.Graph, _pedestrianWalkClips[index % _pedestrianWalkClips.Length]);

            ped.Clip.SetDuration(1e9);

            output.SetSourcePlayable(ped.Clip);

            ped.Graph.Play();

            ped.Rigged = true;
        }

        /// <summary>Scales the walk clip to the pedestrian's ground speed and wraps its time so it loops.</summary>
        private static void AnimatePedestrian(Pedestrian ped, float speed)
        {
            ped.Clip.SetSpeed(speed / WalkClipSpeed);

            var length = ped.Clip.GetAnimationClip().length;

            var time = ped.Clip.GetTime();

            if (time >= length)
            {
                ped.Clip.SetTime(time % length);
            }
        }

        // ------------------------------------------------------------------ collisions

        /// <summary>Fills the list with the nearest active cars within range, closest first, for the sound system.</summary>
        internal void CollectNearbyCars(Vector3 origin, int max, float range, List<NearCar> into)
        {
            into.Clear();

            foreach (var car in _cars)
            {
                if (!car.Active)
                {
                    continue;
                }

                var distance = Vector3.Distance(car.Transform.position, origin);

                if (distance > range)
                {
                    continue;
                }

                var speed = car.Knocked ? car.Rb.linearVelocity.magnitude : car.Speed;

                into.Add(new NearCar { Id = car.Id, Position = car.Transform.position, Speed = speed });
            }

            into.Sort((a, b) => Vector3.SqrMagnitude(a.Position - origin).CompareTo(Vector3.SqrMagnitude(b.Position - origin)));

            if (into.Count > max)
            {
                into.RemoveRange(max, into.Count - max);
            }
        }

        /// <summary>Called by the engine when it hits something at speed. Returns true when a civilian car was knocked away, so the engine keeps part of its speed.</summary>
        internal bool TryImpact(Collider collider, Vector3 point, Vector3 velocity)
        {
            if (!_carByCollider.TryGetValue(collider, out var car) || !car.Active)
            {
                return false;
            }

            KnockCar(car, point, velocity);

            PlayImpact(point, velocity.magnitude);

            return true;
        }

        /// <summary>Hands a struck car to the physics engine: it slides, spins and rebounds off buildings until it is recycled.</summary>
        private void KnockCar(Car car, Vector3 point, Vector3 velocity)
        {
            var body = car.Rb;

            if (car.Knocked)
            {
                body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity + velocity * 0.6f, CarKnockSpeedLimit);
            }
            else
            {
                car.Knocked = true;

                car.KnockTime = 0f;

                car.Rest = 0f;

                body.isKinematic = false;

                body.mass = CarMass;

                body.linearDamping = 0.15f;

                body.angularDamping = 1.5f;

                body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

                body.interpolation = RigidbodyInterpolation.Interpolate;

                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                var thrown = Vector3.ClampMagnitude(velocity * CarKnockFactor, CarKnockSpeedLimit);

                thrown.y = 0f;

                body.linearVelocity = thrown;

                var offset = point - car.Transform.position;

                offset.y = 0f;

                var spin = thrown.sqrMagnitude > 0.01f ? Mathf.Clamp(Vector3.Cross(offset, thrown.normalized).y * 0.8f, -3f, 3f) : 0f;

                body.angularVelocity = new Vector3(0f, spin, 0f);
            }

            Physics.IgnoreCollision(car.Collider, _playerController, true);

            car.IgnoreTimer = IgnoreEngineSeconds;
        }

        /// <summary>Tracks a wrecked car and recycles it once it has been still for a while, is far away or has been down too long.</summary>
        private void StepKnocked(Car car, float dt)
        {
            car.KnockTime += dt;

            if (car.IgnoreTimer > 0f)
            {
                car.IgnoreTimer -= dt;

                if (car.IgnoreTimer <= 0f)
                {
                    Physics.IgnoreCollision(car.Collider, _playerController, false);
                }
            }

            var distance = Vector3.Distance(car.Transform.position, _player.position);

            car.Rest = car.Rb.linearVelocity.magnitude < 0.4f ? car.Rest + dt : 0f;

            if (distance > DespawnDistance || car.KnockTime > WreckSeconds || (car.Rest > WreckRestSeconds && distance > StopRange))
            {
                Despawn(car);
            }
        }

        /// <summary>True when a point lies inside the engine's body, so a walker there has been run over.</summary>
        private bool InEngineFootprint(Vector3 point)
        {
            var local = _player.InverseTransformPoint(point);

            return Mathf.Abs(local.x) < EngineHalfWidth && local.z > EngineRear && local.z < EngineFront && Mathf.Abs(local.y) < 2.5f;
        }

        /// <summary>Launches a struck pedestrian as a tumbling rigid body that ignores the engine's controller.</summary>
        private void KnockPedestrian(Pedestrian ped, Vector3 velocity)
        {
            ped.Knocked = true;

            ped.KnockTime = 0f;

            if (ped.Rigged)
            {
                ped.Clip.SetSpeed(0f);
            }

            if (ped.Rigged && ped.Animator != null)
            {
                var thrown = velocity * 0.95f + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));

                thrown.y = 2f + velocity.magnitude * 0.2f;

                ped.Graph.Stop();

                RagdollBuilder.Build(new RagdollRequest { Animator = ped.Animator, Mass = PedestrianMass, Ignore = _playerController, Velocity = thrown });

                _response.ScaleSpeed(0.92f);

                PlayImpact(ped.Transform.position, velocity.magnitude * 0.5f);

                return;
            }

            var go = ped.Transform.gameObject;

            var body = go.AddComponent<Rigidbody>();

            body.mass = PedestrianMass;

            var capsule = go.AddComponent<CapsuleCollider>();

            capsule.radius = 0.28f;

            capsule.height = 1.7f;

            capsule.center = new Vector3(0f, 0.85f, 0f);

            Physics.IgnoreCollision(capsule, _playerController, true);

            var launched = velocity * 0.95f + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));

            launched.y = 3f + velocity.magnitude * 0.25f;

            body.linearVelocity = launched;

            body.angularVelocity = Random.onUnitSphere * 6f;

            _response.ScaleSpeed(0.92f);

            PlayImpact(ped.Transform.position, velocity.magnitude * 0.5f);
        }

        /// <summary>Leaves a struck pedestrian down for a few seconds, then removes the physics and recycles them.</summary>
        private void StepKnockedPedestrian(Pedestrian ped, float dt, float distance)
        {
            ped.KnockTime += dt;

            if (ped.KnockTime < PedestrianDownSeconds && distance <= DespawnDistance)
            {
                return;
            }

            var go = ped.Transform.gameObject;

            if (ped.Rigged && ped.Animator != null)
            {
                RagdollBuilder.Remove(ped.Animator);

                ped.Graph.Play();
            }

            Destroy(go.GetComponent<CapsuleCollider>());

            Destroy(go.GetComponent<Rigidbody>());

            ped.Knocked = false;

            ped.Active = false;

            go.SetActive(false);
        }

        /// <summary>Plays the thump at the point of impact, louder for harder hits.</summary>
        private void PlayImpact(Vector3 point, float speed)
        {
            AudioSource.PlayClipAtPoint(_impactClip, point, Mathf.Clamp(0.25f + speed / 16f, 0.25f, 1f));
        }

        /// <summary>Synthesizes a short low thump: filtered noise plus a decaying low sine.</summary>
        private static AudioClip CreateImpactClip()
        {
            const int rate = 22050;

            var samples = new float[(int)(rate * 0.6f)];

            var random = new System.Random(7);

            var low = 0f;

            for (var i = 0; i < samples.Length; i++)
            {
                var time = i / (float)rate;

                low += ((float)random.NextDouble() * 2f - 1f - low) * 0.08f;

                var envelope = Mathf.Exp(-time * 9f);

                samples[i] = Mathf.Clamp((low * 1.6f + Mathf.Sin(2f * Mathf.PI * 58f * time) * 0.7f + Mathf.Sin(2f * Mathf.PI * 110f * time) * 0.25f) * envelope * 0.8f, -0.95f, 0.95f);
            }

            var clip = AudioClip.Create("TrafficImpact", samples.Length, 1, rate, false);

            clip.SetData(samples, 0);

            return clip;
        }

        /// <summary>Releases the generated impact sound.</summary>
        private void OnDestroy()
        {
            foreach (var ped in _pedestrians)
            {
                if (ped.Rigged && ped.Graph.IsValid())
                {
                    ped.Graph.Destroy();
                }
            }

            ReleaseClips();
        }

        private void ReleaseClips()
        {
            if (_impactClip != null)
            {
                Destroy(_impactClip);
            }
        }

        // ------------------------------------------------------------------ debug

        /// <summary>Number of cars currently active (used by the verification script).</summary>
        internal int ActiveCars
        {
            get
            {
                var count = 0;

                foreach (var car in _cars)
                {
                    if (car.Active)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Number of pedestrians currently active.</summary>
        internal int ActivePedestrians
        {
            get
            {
                var count = 0;

                foreach (var ped in _pedestrians)
                {
                    if (ped.Active)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }
}
