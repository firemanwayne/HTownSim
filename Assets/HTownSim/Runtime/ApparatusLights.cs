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
        /// <summary>Flash rate in cycles per second (matches the beacon lights in <see cref="FireResponse"/>).</summary>
        private const float FlashHertz = 3f;

        /// <summary>Fraction of each cycle a lens is lit.</summary>
        private const float DutyCycle = 0.45f;

        /// <summary>Offset between the two groups, as a fraction of a cycle.</summary>
        private const float GroupOffset = 0.5f;

        [SerializeField] private FireResponse _response = null!;

        [SerializeField] private Renderer[] _lenses = new Renderer[0];

        [SerializeField] private Material[] _on = new Material[0];

        [SerializeField] private Material[] _off = new Material[0];

        [SerializeField] private int[] _group = new int[0];

        private bool[] _lit = new bool[0];

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

        /// <summary>Lights each lens according to the emergency-light state and its flash group.</summary>
        private void Update()
        {
            var active = _response.LightsActive;

            for (var g = 0; g < 2; g++)
            {
                _pools[g].enabled = active && Mathf.Repeat(Time.time * FlashHertz + g * GroupOffset, 1f) < DutyCycle;
            }

            for (var i = 0; i < _lenses.Length; i++)
            {
                var lit = active && Mathf.Repeat(Time.time * FlashHertz + _group[i] * GroupOffset, 1f) < DutyCycle;

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
