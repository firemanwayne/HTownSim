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
        /// <summary>Flash rate in cycles per second (about the same as the beacon lights in <see cref="FireResponse"/>).</summary>
        private const float FlashHertz = 1.8f;

        /// <summary>Slower flash rate for the front light bar, the rear lamps and the road light pools; the side markers keep <see cref="FlashHertz"/>.</summary>
        private const float FrontRearHertz = 1.15f;

        /// <summary>Lenses with this in their name are the roof light bar.</summary>
        private const string BarLensName = "TwinLED";

        /// <summary>Lenses with this in their name are the side markers.</summary>
        private const string SideLensName = "SideMarker";

        /// <summary>Fraction of each cycle a lens is lit.</summary>
        private const float DutyCycle = 0.45f;

        /// <summary>Offset between the two groups, as a fraction of a cycle.</summary>
        private const float GroupOffset = 0.5f;

        /// <summary>
        /// How far each light's own flash rate may stray from <see cref="FlashHertz"/>, as a fraction. Every light runs at a
        /// slightly different rate, so they drift in and out of step: mostly independent, now and then flashing together.
        /// </summary>
        private const float FlashSpread = 0.07f;

        /// <summary>Spacing that scatters light indices evenly across the spread (the golden ratio fraction).</summary>
        private const float SpreadStep = 0.6180339f;

        [SerializeField] private FireResponse _response = null!;

        [SerializeField] private Renderer[] _lenses = new Renderer[0];

        [SerializeField] private Material[] _on = new Material[0];

        [SerializeField] private Material[] _off = new Material[0];

        [SerializeField] private int[] _group = new int[0];

        private bool[] _lit = new bool[0];

        /// <summary>Which flasher each lens belongs to; lenses of one flasher (a half of the light bar, a rear lamp column) share a phase.</summary>
        private int[] _flasher = new int[0];

        /// <summary>The flasher of the left (0) and right (1) halves of the roof light bar, or -1 when none was found.</summary>
        private readonly int[] _barFlasher = { -1, -1 };

        private float[] _phase = new float[0];

        private float[] _hertz = new float[0];

        private readonly float[] _poolPhase = new float[2];

        private readonly float[] _poolHertz = new float[2];

        /// <summary>Distance ahead of the engine, and the size, of the flashing light pools painted on the road.</summary>
        private const float PoolForward = 10f;

        private const float PoolSide = 3.2f;

        private const float PoolWidth = 8f;

        private const float PoolLength = 14f;

        private const float PoolAlpha = 0.5f;

        private const int PoolTextureSize = 64;

        /// <summary>World size of the glow halo drawn around a lit lens, and its opacity.</summary>
        private const float HaloSize = 1.5f;

        private const float HaloAlpha = 0.9f;

        private Transform[] _halos = new Transform[0];

        private Renderer[] _haloRenderers = new Renderer[0];

        private Material[] _haloMaterials = new Material[0];

        private readonly Renderer[] _pools = new Renderer[2];

        private Texture2D? _poolTexture;

        private readonly Material?[] _poolMaterials = new Material?[2];

        /// <summary>Allocates the per-lens state cache.</summary>
        private void Awake()
        {
            _lit = new bool[_lenses.Length];

            _flasher = new int[_lenses.Length];

            var keys = new System.Collections.Generic.Dictionary<string, int>();

            var bases = new System.Collections.Generic.List<float>();

            var starts = new System.Collections.Generic.List<float>();

            for (var i = 0; i < _lenses.Length; i++)
            {
                var side = _lenses[i].name.Contains(SideLensName);

                var key = side ? SideLensName + i : _lenses[i].name + "|" + _group[i];

                if (!keys.TryGetValue(key, out var id))
                {
                    id = keys.Count;

                    keys[key] = id;

                    bases.Add(side ? FlashHertz : FrontRearHertz);

                    starts.Add(_group[i] * GroupOffset);
                }

                _flasher[i] = id;

                if (_lenses[i].name.Contains(BarLensName) && _group[i] >= 0 && _group[i] < _barFlasher.Length)
                {
                    _barFlasher[_group[i]] = id;
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

                _halos[i].localScale = Vector3.one * HaloSize;
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
                var lit = active && _phase[_flasher[i]] < DutyCycle;

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
