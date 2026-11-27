#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Flashes the HPD patrol unit's lightbar: the red and blue LED modules (the HPD_Flash_Red and HPD_Flash_Blue materials of the
    /// model) take turns, like a wig-wag. It works on its own copies of the materials, so every car flashes on its own beat.
    /// </summary>
    internal sealed class PatrolLightbar : MonoBehaviour
    {
        private const string RedMaterial = "HPD_Flash_Red";

        private const string BlueMaterial = "HPD_Flash_Blue";

        /// <summary>How many red-then-blue cycles per second the patrol lightbar runs. Raise it for a faster flash; lower it for a slower, calmer one.</summary>
        private static float CyclesPerSecond => UnitBehavior.Patrol.FlashHertz;

        /// <summary>How bright a lit lightbar module is (HDR emission, so the bloom picks it up). Raise it for a stronger glow and bloom; lower it for a duller bar.</summary>
        private const float FlashEmission = 6f;

        private const string LensList = "HpdLenses";

        private const string WarningGroup = "Warning";

        /// <summary>Glow halos are sized from the glow list: this many times the lens size. Raise it for bigger glows on the bar; lower it for tight ones.</summary>
        private const float HaloLensScale = 3.6f;

        /// <summary>The smallest a lightbar glow may be (metres). Raise it so every module glows visibly; lower it for finer glows.</summary>
        private const float HaloMinSize = 0.3f;

        /// <summary>The largest a lightbar glow may be (metres). Raise it to let the glows reach further; lower it to keep them small.</summary>
        private const float HaloMaxSize = 0.7f;

        /// <summary>Opacity (0 to 1) of the lightbar glows. Raise it for a stronger glow; lower it for a faint one.</summary>
        private const float HaloAlpha = 0.8f;

        /// <summary>How far (metres) in front of the lens, toward the camera, each glow sits. Raise it so the glow clears the bar's housing; lower it to seat the glow against the lens.</summary>
        private const float HaloLift = 0.1f;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private readonly List<GlowQuad> _redGlows = new List<GlowQuad>();

        private readonly List<GlowQuad> _blueGlows = new List<GlowQuad>();

        private readonly List<Material> _glowMaterials = new List<Material>();

        private Texture2D? _glowTexture;

        private Transform? _glowRoot;

        private readonly List<Material> _red = new List<Material>();

        private readonly List<Material> _blue = new List<Material>();

        private Color _redOn;

        private Color _blueOn;

        private float _offset;

        private bool _redLit;

        private void Awake()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                // Reading .materials makes this renderer's own copies, so other cars keep their own state.
                var materials = renderer.materials;

                foreach (var material in materials)
                {
                    if (material.name.StartsWith(RedMaterial))
                    {
                        _red.Add(material);

                        _redOn = BrightEmission(material);

                        material.EnableKeyword("_EMISSION");
                    }
                    else if (material.name.StartsWith(BlueMaterial))
                    {
                        _blue.Add(material);

                        _blueOn = BrightEmission(material);

                        material.EnableKeyword("_EMISSION");
                    }
                }
            }

            _offset = Random.value;

            BuildGlows();
        }

        /// <summary>The emission colour of a material at the flash brightness.</summary>
        private static Color BrightEmission(Material material)
        {
            var colour = material.GetColor(EmissionColor);

            return colour * (FlashEmission / Mathf.Max(0.01f, colour.maxColorComponent));
        }

        /// <summary>Creates the camera-facing glow quads for the lightbar modules, from the glow list of the model.</summary>
        private void BuildGlows()
        {
            var asset = Resources.Load<TextAsset>(LensList);

            if (asset == null)
            {
                return;
            }

            var list = JsonUtility.FromJson<GlowList>(asset.text);

            _glowRoot = FindChild(transform, list.root);

            if (_glowRoot == null)
            {
                return;
            }

            _glowTexture = LensFlasher.SoftDisc();

            foreach (var lens in list.lenses)
            {
                var red = lens.material == RedMaterial;

                if (lens.group != WarningGroup || (!red && lens.material != BlueMaterial))
                {
                    continue;
                }

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);

                Destroy(quad.GetComponent<Collider>());

                quad.name = "LensHalo_" + lens.name;

                quad.transform.SetParent(_glowRoot, false);

                var colour = red ? new Color(1f, 0.12f, 0.08f) : new Color(0.15f, 0.35f, 1f);

                colour.a = HaloAlpha;

                var material = new Material(Shader.Find("Sprites/Default")) { mainTexture = _glowTexture, color = colour, name = "PatrolHalo" };

                var renderer = quad.GetComponent<Renderer>();

                renderer.sharedMaterial = material;

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                renderer.receiveShadows = false;

                renderer.enabled = false;

                _glowMaterials.Add(material);

                (red ? _redGlows : _blueGlows).Add(new GlowQuad(quad.transform, renderer, new Vector3(lens.x, lens.y, lens.z), Mathf.Clamp(lens.size * HaloLensScale, HaloMinSize, HaloMaxSize)));
            }
        }

        private static Transform? FindChild(Transform parent, string name)
        {
            foreach (var child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private void Update()
        {
            var lit = Mathf.Repeat((Time.time * CyclesPerSecond) + _offset, 1f) < 0.5f;

            if (lit == _redLit && Time.frameCount > 1)
            {
                return;
            }

            _redLit = lit;

            Set(_red, lit ? _redOn : Color.black);

            Set(_blue, lit ? Color.black : _blueOn);
        }

        /// <summary>Keeps the glows of the lit modules just off their lenses, turned to the camera.</summary>
        private void LateUpdate()
        {
            var camera = Camera.main;

            Place(_redGlows, camera, _redLit);

            Place(_blueGlows, camera, !_redLit);
        }

        private static void Place(List<GlowQuad> glows, Camera? camera, bool lit)
        {
            foreach (var glow in glows)
            {
                glow.Renderer.enabled = lit && camera != null;

                if (!glow.Renderer.enabled)
                {
                    continue;
                }

                var anchor = glow.Transform.parent.TransformPoint(glow.Local);

                glow.Transform.position = anchor + ((camera!.transform.position - anchor).normalized * HaloLift);

                glow.Transform.rotation = camera.transform.rotation;

                glow.Transform.localScale = Vector3.one * glow.Size;
            }
        }

        [System.Serializable]
        private sealed class GlowList
        {
            public string root = "";

            public GlowLens[] lenses = new GlowLens[0];
        }

        [System.Serializable]
        private sealed class GlowLens
        {
            public string name = "";

            public string group = "";

            public string material = "";

            public float x;

            public float y;

            public float z;

            public float size;
        }

        private sealed class GlowQuad
        {
            internal GlowQuad(Transform transform, Renderer renderer, Vector3 local, float size)
            {
                Transform = transform;

                Renderer = renderer;

                Local = local;

                Size = size;
            }

            internal Transform Transform { get; }

            internal Renderer Renderer { get; }

            internal Vector3 Local { get; }

            internal float Size { get; }
        }

        private static void Set(List<Material> materials, Color color)
        {
            foreach (var material in materials)
            {
                material.SetColor(EmissionColor, color);
            }
        }

        private void OnDestroy()
        {
            // The per-car material copies are not owned by any asset.
            foreach (var material in _red)
            {
                Destroy(material);
            }

            foreach (var material in _blue)
            {
                Destroy(material);
            }

            foreach (var material in _glowMaterials)
            {
                Destroy(material);
            }

            if (_glowTexture != null)
            {
                Destroy(_glowTexture);
            }
        }
    }
}
