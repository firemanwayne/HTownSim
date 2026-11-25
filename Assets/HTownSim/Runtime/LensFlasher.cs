#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Emergency lights for an apparatus whose lenses are modelled into its mesh (Ladder 07): the profile names lens
    /// materials in two flash groups that alternate while the emergency lights are on, and steady lenses that burn with
    /// the headlights. The driven model's renderers get private copies of those materials, so the asset materials and
    /// any parked copy of the model are never touched.
    /// </summary>
    internal sealed class LensFlasher : MonoBehaviour
    {
        private const float FlashHertz = 1.6f;

        /// <summary>Within each half of the cycle a group double-pulses, like LED warning lights.</summary>
        private const float PulseDuty = 0.32f;

        private const float FlashEmission = 6f;

        private const float SteadyEmission = 2.5f;

        private FireResponse _response = null!;

        private readonly List<LensMaterial> _groupA = new();

        private readonly List<LensMaterial> _groupB = new();

        private readonly List<LensMaterial> _steady = new();

        private readonly List<Material> _copies = new();

        /// <summary>Soft camera-facing glow quads for the larger lenses of a model with its lenses in the mesh (see ApparatusProfile.LensHalos).</summary>
        private readonly List<Halo> _halos = new();

        private Texture2D? _haloTexture;

        /// <summary>Halos are sized from their lens (this many times its longest side) and kept between these limits: the ambulance's lenses are small, so its glows are too.</summary>
        private const float HaloLensScale = 3.6f;

        private const float HaloMinSize = 0.3f;

        private const float HaloMaxSize = 0.7f;

        private const float HaloAlpha = 0.8f;

        /// <summary>How far in front of the lens (towards the camera) a halo sits.</summary>
        private const float HaloLift = 0.1f;

        /// <summary>Lens groups that get a glow when steady (the headlights, fog lights and brake lights); the small marker lights do not.</summary>
        private static readonly string[] SteadyGlowGroups = { "Headlight", "Fog", "Stop" };

        private float _phase;

        /// <summary>True when the last update lit group A, for verification.</summary>
        internal bool GroupALit { get; private set; }

        /// <summary>True when the last update lit group B.</summary>
        internal bool GroupBLit { get; private set; }

        /// <summary>Number of lens materials taken over, for verification.</summary>
        internal int LensMaterialCount => _groupA.Count + _groupB.Count + _steady.Count;

        /// <summary>Copies the named lens materials onto the active model's renderers.</summary>
        internal void Initialize(FireResponse response, ApparatusProfileValues profile)
        {
            _response = response;

            var copies = new Dictionary<Material, Material>();

            foreach (var renderer in GetComponentsInChildren<Renderer>(false))
            {
                var materials = renderer.sharedMaterials;

                var changed = false;

                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];

                    if (source == null)
                    {
                        continue;
                    }

                    var group = GroupOf(source.name, profile);

                    if (group is null)
                    {
                        continue;
                    }

                    if (!copies.TryGetValue(source, out var copy))
                    {
                        copy = new Material(source) { name = source.name + "_Flash" };

                        copy.EnableKeyword("_EMISSION");

                        copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

                        copies[source] = copy;

                        _copies.Add(copy);

                        var lens = new LensMaterial(copy, source.HasProperty("_EmissionColor") ? source.GetColor("_EmissionColor") : Color.black, source.color);

                        (group == 'A' ? _groupA : group == 'B' ? _groupB : _steady).Add(lens);
                    }

                    materials[i] = copy;

                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }

            BuildHalos(profile);
        }

        /// <summary>Creates the glow quads listed by the profile's lens file, each tied to the flash group of its lens material.</summary>
        private void BuildHalos(ApparatusProfileValues profile)
        {
            if (string.IsNullOrEmpty(profile.LensHalos))
            {
                return;
            }

            var asset = Resources.Load<TextAsset>(profile.LensHalos);

            if (asset == null)
            {
                return;
            }

            var list = JsonUtility.FromJson<HaloList>(asset.text);

            var root = FindChild(transform, list.root);

            if (root == null)
            {
                return;
            }

            _haloTexture = SoftDisc();

            foreach (var lens in list.lenses)
            {
                var group = GroupOf(lens.material, profile);

                if (group is null || (group == 'S' && System.Array.IndexOf(SteadyGlowGroups, lens.group) < 0))
                {
                    continue;
                }

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);

                Destroy(quad.GetComponent<Collider>());

                quad.name = "LensHalo_" + lens.name;

                quad.transform.SetParent(root, false);

                var colour = lens.material.Contains("Red") ? new Color(1f, 0.12f, 0.08f) : lens.material.Contains("Amber") ? new Color(1f, 0.6f, 0.1f) : new Color(1f, 0.95f, 0.85f);

                colour.a = HaloAlpha;

                var material = new Material(Shader.Find("Sprites/Default")) { mainTexture = _haloTexture, color = colour, name = "LensHalo" };

                var renderer = quad.GetComponent<Renderer>();

                renderer.sharedMaterial = material;

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                renderer.receiveShadows = false;

                renderer.enabled = false;

                _copies.Add(material);

                _halos.Add(new Halo(quad.transform, renderer, new Vector3(lens.x, lens.y, lens.z), Mathf.Clamp(lens.size * HaloLensScale, HaloMinSize, HaloMaxSize), group.Value));
            }
        }

        private static Transform? FindChild(Transform parent, string name)
        {
            foreach (var child in parent.GetComponentsInChildren<Transform>(false))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>A soft round glow (bright centre, smooth fall-off to clear).</summary>
        internal static Texture2D SoftDisc()
        {
            const int size = 64;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Mathf.Clamp01(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f));

                    var a = Mathf.Pow(1f - d, 1.5f);

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            texture.SetPixels32(pixels);

            texture.Apply();

            return texture;
        }

        /// <summary>Keeps each lit glow just off its lens, turned to the camera.</summary>
        private void LateUpdate()
        {
            var camera = Camera.main;

            foreach (var halo in _halos)
            {
                halo.Renderer.enabled = camera != null && (halo.Group == 'A' ? GroupALit : halo.Group == 'B' ? GroupBLit : _response.HeadlightsActive);

                if (!halo.Renderer.enabled)
                {
                    continue;
                }

                var anchor = halo.Transform.parent.TransformPoint(halo.Local);

                var toCamera = (camera!.transform.position - anchor).normalized;

                halo.Transform.position = anchor + toCamera * HaloLift;

                halo.Transform.rotation = camera.transform.rotation;

                halo.Transform.localScale = Vector3.one * halo.Size;
            }
        }

        private static char? GroupOf(string materialName, ApparatusProfileValues profile)
        {
            if (System.Array.IndexOf(profile.FlashGroupA, materialName) >= 0)
            {
                return 'A';
            }

            if (System.Array.IndexOf(profile.FlashGroupB, materialName) >= 0)
            {
                return 'B';
            }

            return System.Array.IndexOf(profile.SteadyLenses, materialName) >= 0 ? 'S' : null;
        }

        /// <summary>Alternates the two groups while the emergency lights are on; steady lenses follow the headlights.</summary>
        private void Update()
        {
            var active = _response.LightsActive;

            _phase = Mathf.Repeat(_phase + FlashHertz * Time.deltaTime, 1f);

            var half = _phase < 0.5f ? _phase * 2f : (_phase - 0.5f) * 2f;

            var pulse = half < PulseDuty || (half > 0.5f && half < 0.5f + PulseDuty);

            var aLit = active && _phase < 0.5f && pulse;

            var bLit = active && _phase >= 0.5f && pulse;

            GroupALit = aLit;

            GroupBLit = bLit;

            Set(_groupA, aLit, FlashEmission);

            Set(_groupB, bLit, FlashEmission);

            Set(_steady, _response.HeadlightsActive, SteadyEmission);
        }

        private static void Set(List<LensMaterial> lenses, bool lit, float strength)
        {
            foreach (var lens in lenses)
            {
                lens.Copy.SetColor("_EmissionColor", lit ? lens.Colour * strength : lens.BaseEmission);
            }
        }

        /// <summary>Releases the material copies.</summary>
        private void OnDestroy()
        {
            foreach (var copy in _copies)
            {
                Destroy(copy);
            }

            if (_haloTexture != null)
            {
                Destroy(_haloTexture);
            }
        }

        /// <summary>One glow quad: its lens position in the model's space, size and flash group (A, B or S for steady).</summary>
        private sealed class Halo
        {
            internal Halo(Transform transform, Renderer renderer, Vector3 local, float size, char group)
            {
                Transform = transform;

                Renderer = renderer;

                Local = local;

                Size = size;

                Group = group;
            }

            internal Transform Transform { get; }

            internal Renderer Renderer { get; }

            internal Vector3 Local { get; }

            internal float Size { get; }

            internal char Group { get; }
        }

        /// <summary>The lens glow file: the model root's name and one entry per lens (model-space position and size).</summary>
        [System.Serializable]
        private sealed class HaloList
        {
            public string root = "";

            public HaloLens[] lenses = new HaloLens[0];
        }

        [System.Serializable]
        private sealed class HaloLens
        {
            public string name = "";

            public string group = "";

            public string material = "";

            public float x;

            public float y;

            public float z;

            public float size;
        }

        /// <summary>A lens material copy with its unlit emission and its lens colour.</summary>
        private sealed class LensMaterial
        {
            internal LensMaterial(Material copy, Color baseEmission, Color colour)
            {
                Copy = copy;

                BaseEmission = baseEmission;

                Colour = colour;
            }

            internal Material Copy { get; }

            internal Color BaseEmission { get; }

            internal Color Colour { get; }
        }
    }
}
