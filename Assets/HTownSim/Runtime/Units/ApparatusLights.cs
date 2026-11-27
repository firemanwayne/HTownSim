#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Flashes the warning-light lenses on the engine while the player's emergency lights are on, in step with the
    /// beacon lights, and leaves them dull when the lights are off. Lenses are split into two alternating groups.
    /// </summary>
    internal sealed class ApparatusLights : MonoBehaviour
    {
        /// <summary>Flash rate of the engine's emergency lights, in cycles per second: an engine's own, defined in <see cref="UnitBehavior.Engine"/>.</summary>
        private static float FlashHertz => UnitBehavior.Engine.FlashHertz;

        /// <summary>A slower flash rate (cycles per second) for the front light bar, the rear lamps and the road light pools; the side markers keep FlashHertz. Raise it to speed those lights up; lower it to slow them.</summary>
        private static float FrontRearHertz => UnitBehavior.Engine.FrontRearHertz;

        /// <summary>Lenses with this in their name are the roof light bar.</summary>
        private const string BarLensName = "TwinLED";

        /// <summary>
        /// The light bar's lens meshes are named EmergencyLighting_Lens0 to Lens47: 12 columns of 4, where in each column the
        /// even meshes are the rear face and the odd ones the front, and the first two are the lower row and the last two the upper.
        /// </summary>
        private const string BarMeshPrefix = "EmergencyLighting_Lens";

        /// <summary>
        /// The front bumper lens pods, split into individual lights. Lower pods: the inner light is the high beam and the outer the
        /// low beam. Upper pods: two red emergency lights each. Names end in _L or _R for the truck's side.
        /// </summary>
        private const string FrontLensPrefix = "FrontLens";

        private const string LowBeamPrefix = "FrontLensLow";

        /// <summary>Amber roof clearance lights: they come on with the headlights and stay steady.</summary>
        private const string ClearancePrefix = "ClearanceLight";

        /// <summary>Amber front turn signals (names end in _L or _R); they flash with the turn and hazard switches, not the emergency lights.</summary>
        private const string TurnSignalPrefix = "TurnSignal";

        /// <summary>How fast the turn signals and hazards blink, in blinks per second. Raise it for a quicker blink; lower it for a slower one.</summary>
        private const float SignalHertz = 1.5f;

        /// <summary>The fraction (0 to 1) of each blink the turn signal is on. Raise it for longer on-time; lower it for brief blinks.</summary>
        private const float SignalDuty = 0.5f;
        private const string HighBeamPrefix = "FrontLensHigh";
        private const string RightSuffix = "_R";

        /// <summary>Wig-wag rate of the high beams, which alternate left and right while the emergency lights are on, in cycles per second. Raise it for faster alternating; lower it for a slow wig-wag.</summary>
        private const float WigWagHertz = 1.4f;

        private const int BarLenses = 48;
        private const int BarColumns = 12;
        private const int LensesPerColumn = 4;

        /// <summary>The fraction of a flash cycle the lower lens of a red column trails the upper lens, so red columns roll from top to bottom. Raise it for a slower, more visible roll; lower it to 0 for no roll.</summary>
        private const float RollDelay = 0.13f;

        /// <summary>Lenses with this in their name are the side markers.</summary>
        private const string SideLensName = "SideMarker";

        /// <summary>The fraction (0 to 1) of each cycle a lens is lit. Raise it for longer, brighter-looking flashes; lower it for short, sharp ones.</summary>
        private const float DutyCycle = 0.45f;

        /// <summary>The offset between the two flash groups, as a fraction of a cycle (0.5 means the groups alternate). Move it toward 0 to make the groups flash together; toward 0.5 for a clean alternate.</summary>
        private const float GroupOffset = 0.5f;

        /// <summary>How far each light's own flash rate may stray from FlashHertz, as a fraction. Raise it so lights drift in and out of step faster and look more random; lower it so they stay locked together.</summary>
        private const float FlashSpread = 0.07f;

        /// <summary>Spacing that scatters light indices evenly across the spread (the golden ratio fraction). Leave it alone unless the flashing starts to look patterned.</summary>
        private const float SpreadStep = 0.6180339f;

        [SerializeField] private HTownSim.Runtime.Contracts.VehicleStateBase _response = null!;

        [SerializeField] private Renderer[] _lenses = new Renderer[0];

        [SerializeField] private Material[] _on = new Material[0];

        [SerializeField] private Material[] _off = new Material[0];

        [SerializeField] private int[] _group = new int[0];

        private bool[] _lit = new bool[0];

        /// <summary>Which flasher each lens belongs to; lenses of one flasher (a half of the light bar, a rear lamp column) share a phase.</summary>
        private int[] _flasher = new int[0];

        /// <summary>The flasher of the left (0) and right (1) halves of the roof light bar, or -1 when none was found.</summary>
        private readonly int[] _barFlasher = { -1, -1 };

        /// <summary>True for lenses on the headlight circuit (low beams and clearance lights): steady, lit whenever the headlights are on.</summary>
        private bool[] _steady = new bool[0];

        /// <summary>-1 for a left turn signal lens, +1 for a right one, 0 for every other lens.</summary>
        private int[] _signalSide = new int[0];

        /// <summary>How far each lens trails its flasher's phase, as a fraction of a cycle.</summary>
        private float[] _delay = new float[0];

        private float[] _phase = new float[0];

        private float[] _hertz = new float[0];

        private readonly float[] _poolPhase = new float[2];

        private readonly float[] _poolHertz = new float[2];

        /// <summary>How far ahead of the engine (metres) the flashing light pools on the road start. Raise it to throw the pools further out; lower it to bring them close to the engine.</summary>
        private const float PoolForward = 10f;

        /// <summary>How far to the side (metres) of the engine's centre line each light pool sits. Raise it to push the pools wider apart; lower it to bring them closer.</summary>
        private const float PoolSide = 3.2f;

        /// <summary>Width (metres) of each light pool on the road. Raise it for broader washes of light; lower it for narrow ones.</summary>
        private const float PoolWidth = 8f;

        /// <summary>Length (metres) of each light pool along the road. Raise it for longer washes; lower it for short ones.</summary>
        private const float PoolLength = 14f;

        /// <summary>How strong (0 to 1) the light pools are on the road. Raise it for a stronger red wash; lower it to make them faint.</summary>
        private const float PoolAlpha = 0.5f;

        private const int PoolTextureSize = 64;

        /// <summary>World size (metres) of the glow halo drawn around a lit lens. Raise it for a bigger bloom around each light; lower it for tight glows.</summary>
        private const float HaloSize = 1.5f;

        /// <summary>Halos are sized from their lens: this many times its longest side. Raise it so small lenses get larger glows; lower it for tighter glows.</summary>
        private const float HaloLensScale = 3.2f;

        /// <summary>The smallest a lens halo may be (metres). Raise it so even tiny lenses glow visibly; lower it to let tiny lenses have tiny glows.</summary>
        private const float HaloMinSize = 0.3f;

        /// <summary>The largest a lens halo may be (metres). Raise it to let big lenses bloom more; lower it to stop large glows swamping the truck.</summary>
        private const float HaloMaxSize = 0.8f;

        /// <summary>Opacity (0 to 1) of the lens halos. Raise it for a stronger glow; lower it for a faint one (set it to 0 to switch the halos off).</summary>
        private const float HaloAlpha = 0.55f;

        private Transform[] _halos = new Transform[0];

        private float[] _haloSizes = new float[0];

        private Renderer[] _haloRenderers = new Renderer[0];

        private Material[] _haloMaterials = new Material[0];

        private readonly Renderer[] _pools = new Renderer[2];

        private Texture2D? _poolTexture;

        private readonly Material?[] _poolMaterials = new Material?[2];

        /// <summary>Allocates the per-lens state cache.</summary>
        private void Awake()
        {
            // Headless servers have no graphics device and strip shaders, so the lamp visuals cannot be built.
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }

            _lit = new bool[_lenses.Length];

            _flasher = new int[_lenses.Length];

            _delay = new float[_lenses.Length];

            _steady = new bool[_lenses.Length];

            _signalSide = new int[_lenses.Length];

            FindBarMaterials(out var redOn, out var redOff, out var whiteOn, out var whiteOff);

            var keys = new System.Collections.Generic.Dictionary<string, int>();

            var bases = new System.Collections.Generic.List<float>();

            var starts = new System.Collections.Generic.List<float>();

            for (var i = 0; i < _lenses.Length; i++)
            {
                var bar = BarIndex(_lenses[i]);

                var side = bar < 0 && _lenses[i].name.Contains(SideLensName);

                string key;

                var start = _group[i] * GroupOffset;

                var signal = bar < 0 && _lenses[i].name.StartsWith(TurnSignalPrefix);

                var clearance = bar < 0 && _lenses[i].name.StartsWith(ClearancePrefix);

                var front = bar < 0 && (_lenses[i].name.StartsWith(FrontLensPrefix) || clearance);

                var baseHertz = side ? FlashHertz : FrontRearHertz;

                if (signal)
                {
                    _signalSide[i] = _lenses[i].name.EndsWith(RightSuffix) ? 1 : -1;

                    key = TurnSignalPrefix;

                    baseHertz = SignalHertz;

                    start = 0f;
                }
                else if (front)
                {
                    var lowBeam = _lenses[i].name.StartsWith(LowBeamPrefix);

                    var highBeam = _lenses[i].name.StartsWith(HighBeamPrefix);

                    _steady[i] = lowBeam || clearance;

                    key = highBeam ? HighBeamPrefix : _lenses[i].name;

                    _delay[i] = highBeam && _lenses[i].name.EndsWith(RightSuffix) ? 0.5f : 0f;

                    baseHertz = highBeam ? WigWagHertz : FrontRearHertz;

                    start = 0f;
                }
                else if (bar >= 0)
                {
                    var column = bar / LensesPerColumn;

                    var upper = bar % LensesPerColumn >= 2;

                    var red = Mathf.Min(column, BarColumns - 1 - column) % 2 == 0;

                    key = "Bar" + column;

                    start = Mathf.Repeat(column * 0.173f, 1f);

                    _delay[i] = red && !upper ? RollDelay : 0f;

                    if (redOn != null && whiteOn != null)
                    {
                        _on[i] = red ? redOn : whiteOn;

                        _off[i] = red ? redOff : whiteOff;

                        _lenses[i].sharedMaterial = _off[i];
                    }
                }
                else
                {
                    key = side ? SideLensName + i : _lenses[i].name + "|" + _group[i];
                }

                if (!keys.TryGetValue(key, out var id))
                {
                    id = keys.Count;

                    keys[key] = id;

                    bases.Add(baseHertz);

                    starts.Add(start);
                }

                _flasher[i] = id;

                if (bar >= 0)
                {
                    var column = bar / LensesPerColumn;

                    if (column == 0)
                    {
                        _barFlasher[0] = id;
                    }
                    else if (column == BarColumns - 1)
                    {
                        _barFlasher[1] = id;
                    }
                }
            }

            _phase = starts.ToArray();

            _hertz = new float[bases.Count];

            for (var f = 0; f < _hertz.Length; f++)
            {
                _hertz[f] = bases[f] * (1f + FlashSpread * (Mathf.Repeat(f * SpreadStep, 1f) * 2f - 1f));
            }

            for (var g = 0; g < 2; g++)
            {
                _poolHertz[g] = FrontRearHertz * (1f + FlashSpread * (g == 0 ? -0.8f : 0.9f));

                _poolPhase[g] = g * GroupOffset;
            }

            BuildPools();

            BuildHalos();
        }

        /// <summary>Creates a soft camera-facing glow quad for every lens, shown only while the lens is lit.</summary>
        private void BuildHalos()
        {
            _halos = new Transform[_lenses.Length];

            _haloSizes = new float[_lenses.Length];

            _haloRenderers = new Renderer[_lenses.Length];

            _haloMaterials = new Material[_lenses.Length];

            for (var i = 0; i < _lenses.Length; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);

                Destroy(quad.GetComponent<Collider>());

                quad.name = "LensHalo";

                quad.transform.SetParent(transform, false);

                var tint = _on[i].color;

                tint.a = HaloAlpha;

                _haloMaterials[i] = new Material(Shader.Find("Sprites/Default")) { mainTexture = _poolTexture, color = tint, name = "LensHalo" + i };

                var renderer = quad.GetComponent<Renderer>();

                renderer.sharedMaterial = _haloMaterials[i];

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                renderer.receiveShadows = false;

                renderer.enabled = false;

                _halos[i] = quad.transform;

                var extents = _lenses[i].bounds.size;

                _haloSizes[i] = Mathf.Clamp(Mathf.Max(extents.x, extents.y, extents.z) * HaloLensScale, HaloMinSize, HaloMaxSize);

                _haloRenderers[i] = renderer;
            }
        }

        /// <summary>Keeps each halo on its lens and turned to face the camera.</summary>
        private void LateUpdate()
        {
            var camera = Camera.main;

            if (camera == null)
            {
                return;
            }

            for (var i = 0; i < _halos.Length; i++)
            {
                if (!_haloRenderers[i].enabled)
                {
                    continue;
                }

                var toCamera = (camera.transform.position - _lenses[i].transform.position).normalized;

                _halos[i].position = _lenses[i].transform.position + toCamera * 0.12f;

                _halos[i].rotation = camera.transform.rotation;

                _halos[i].localScale = Vector3.one * _haloSizes[i];
            }
        }

        /// <summary>Creates two soft flashing light pools on the road ahead: red on the left, white on the right.</summary>
        private void BuildPools()
        {
            _poolTexture = new Texture2D(PoolTextureSize, PoolTextureSize, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

            var pixels = new Color32[PoolTextureSize * PoolTextureSize];

            for (var y = 0; y < PoolTextureSize; y++)
            {
                for (var x = 0; x < PoolTextureSize; x++)
                {
                    var u = (x + 0.5f) / PoolTextureSize * 2f - 1f;

                    var v = (y + 0.5f) / PoolTextureSize * 2f - 1f;

                    var falloff = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));

                    pixels[y * PoolTextureSize + x] = new Color32(255, 255, 255, (byte)(255f * falloff * falloff));
                }
            }

            _poolTexture.SetPixels32(pixels);

            _poolTexture.Apply();

            var colours = new[] { new Color(1f, 0.12f, 0.1f, PoolAlpha), new Color(1f, 0.95f, 0.85f, PoolAlpha) };

            for (var i = 0; i < 2; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);

                Destroy(quad.GetComponent<Collider>());

                quad.name = i == 0 ? "LightPoolLeft" : "LightPoolRight";

                quad.transform.SetParent(transform, false);

                quad.transform.localPosition = new Vector3((i == 0 ? -1f : 1f) * PoolSide, -0.1f, PoolForward);

                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                quad.transform.localScale = new Vector3(PoolWidth, PoolLength, 1f);

                var material = new Material(Shader.Find("Sprites/Default")) { mainTexture = _poolTexture, color = colours[i], name = "LightPool" + i };

                _poolMaterials[i] = material;

                var renderer = quad.GetComponent<Renderer>();

                renderer.sharedMaterial = material;

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                renderer.receiveShadows = false;

                renderer.enabled = false;

                _pools[i] = renderer;
            }
        }

        /// <summary>Releases the generated texture and materials.</summary>
        private void OnDestroy()
        {
            if (_poolTexture != null)
            {
                Destroy(_poolTexture);
            }

            foreach (var material in _haloMaterials)
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }

            foreach (var material in _poolMaterials)
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }
        }

        /// <summary>The bar lens number from the mesh name (EmergencyLighting_Lens47 is 47), or -1 for any other lens.</summary>
        private static int BarIndex(Renderer lens)
        {
            var filter = lens.GetComponent<MeshFilter>();

            if (filter == null || filter.sharedMesh == null)
            {
                return -1;
            }

            var meshName = filter.sharedMesh.name;

            if (!meshName.StartsWith(BarMeshPrefix) || !int.TryParse(meshName.Substring(BarMeshPrefix.Length), out var number))
            {
                return -1;
            }

            return number >= 0 && number < BarLenses ? number : -1;
        }

        /// <summary>Finds the red and white on and off lens materials already used by the lenses.</summary>
        private void FindBarMaterials(out Material? redOn, out Material? redOff, out Material? whiteOn, out Material? whiteOff)
        {
            redOn = null;

            redOff = null;

            whiteOn = null;

            whiteOff = null;

            for (var i = 0; i < _lenses.Length && i < _on.Length && i < _off.Length; i++)
            {
                if (redOn == null && _on[i].name.Contains("Red"))
                {
                    redOn = _on[i];

                    redOff = _off[i];
                }

                if (whiteOn == null && _on[i].name.Contains("White"))
                {
                    whiteOn = _on[i];

                    whiteOff = _off[i];
                }
            }
        }

        /// <summary>True while the given half of the roof light bar is in the lit part of its flash, so the beacon lights can match it.</summary>
        internal bool BarLit(int half)
        {
            var id = half >= 0 && half < _barFlasher.Length ? _barFlasher[half] : -1;

            return id >= 0 && _phase.Length > id && _phase[id] < DutyCycle;
        }

        /// <summary>Lights each lens according to the emergency-light state and its flash group.</summary>
        private void Update()
        {
            var active = _response.LightsActive;

            var headlights = _response.HeadlightsActive;

            var leftSignal = _response.LeftSignalOn;

            var rightSignal = _response.RightSignalOn;

            for (var g = 0; g < 2; g++)
            {
                _poolPhase[g] = Mathf.Repeat(_poolPhase[g] + _poolHertz[g] * Time.deltaTime, 1f);

                _pools[g].enabled = active && _poolPhase[g] < DutyCycle;
            }

            for (var f = 0; f < _phase.Length; f++)
            {
                _phase[f] = Mathf.Repeat(_phase[f] + _hertz[f] * Time.deltaTime, 1f);
            }

            for (var i = 0; i < _lenses.Length; i++)
            {
                // Lenses of a model that is not the one being driven (e.g. the engine's while driving the ambulance) stay dark, with no glow.
                if (!_lenses[i].gameObject.activeInHierarchy)
                {
                    _lit[i] = false;

                    _haloRenderers[i].enabled = false;

                    continue;
                }

                bool lit;

                if (_signalSide[i] != 0)
                {
                    lit = (_signalSide[i] < 0 ? leftSignal : rightSignal) && Mathf.Repeat(_phase[_flasher[i]], 1f) < SignalDuty;
                }
                else
                {
                    lit = _steady[i] ? headlights : active && Mathf.Repeat(_phase[_flasher[i]] - _delay[i], 1f) < DutyCycle;
                }

                if (lit == _lit[i])
                {
                    continue;
                }

                _lit[i] = lit;

                _lenses[i].sharedMaterial = lit ? _on[i] : _off[i];

                _haloRenderers[i].enabled = lit;
            }
        }
    }
}
