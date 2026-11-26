#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The warning lights and the siren of a computer-driven unit. It finds the unit's lens materials by name (the models name them
    /// differently: Engine07Clean_LensRed, Amb07_LensRedWarn, L07_Red_lenses, DC08_Flash_RedA ...), sorts them into two groups
    /// that flash in turn, and gives the unit its own copies so no other unit of the same model is touched. While on, the lenses
    /// glow bright enough for the bloom, and the siren (<see cref="AiSiren"/>) plays from the unit.
    /// </summary>
    internal sealed class AiEmergencyLights : MonoBehaviour
    {
        /// <summary>Alternations per second of a computer-driven unit's warning lights. Raise it for faster flashing; lower it for slower.</summary>
        private const float FlashHertz = 3f;

        /// <summary>How bright a lit lens glows (HDR emission, so the bloom picks it up). Raise it for a stronger glow; lower it for a dull one.</summary>
        private const float FlashEmission = 6f;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private readonly List<Material> _groupA = new List<Material>();

        private readonly List<Material> _groupB = new List<Material>();

        private readonly List<Color> _colourA = new List<Color>();

        private readonly List<Color> _colourB = new List<Color>();

        private AiSiren? _siren;

        private bool _on;

        private float _offset;

        /// <summary>True while the lights are flashing.</summary>
        internal bool IsOn => _on;

        private void Awake()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.materials;

                foreach (var material in materials)
                {
                    var group = GroupOf(material.name);

                    if (group == 0)
                    {
                        continue;
                    }

                    material.EnableKeyword("_EMISSION");

                    var colour = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;

                    var lit = colour * (FlashEmission / Mathf.Max(0.05f, colour.maxColorComponent));

                    (group == 1 ? _groupA : _groupB).Add(material);

                    (group == 1 ? _colourA : _colourB).Add(lit);
                }
            }

            _siren = gameObject.AddComponent<AiSiren>();

            _offset = Random.value;
        }

        /// <summary>Turns the flashing and the siren on or off.</summary>
        internal void Set(bool on, bool siren)
        {
            _on = on;

            if (!on)
            {
                Apply(_groupA, _colourA, false);

                Apply(_groupB, _colourB, false);
            }

            if (_siren != null)
            {
                _siren.SetPlaying(siren);
            }
        }

        private void Update()
        {
            if (!_on)
            {
                return;
            }

            var first = Mathf.Repeat((Time.time * FlashHertz) + _offset, 1f) < 0.5f;

            Apply(_groupA, _colourA, first);

            Apply(_groupB, _colourB, !first);
        }

        private static void Apply(List<Material> materials, List<Color> colours, bool lit)
        {
            for (var i = 0; i < materials.Count; i++)
            {
                materials[i].SetColor(EmissionColor, lit ? colours[i] : Color.black);
            }
        }

        /// <summary>1 or 2 for a lens material that flashes in the first or second group, 0 for any other.</summary>
        private static int GroupOf(string materialName)
        {
            var name = materialName.ToLowerInvariant();

            if (name.Contains("scene") || name.Contains("steady") || name.Contains("headlight") || name.Contains("lamp") || name.Contains("optical") || name.Contains("amber") || name.Contains("white"))
            {
                return 0;
            }

            if (name.Contains("warn2") || name.Contains("redb") || name.Contains("clear_led") || name.Contains("lensclear") || name.Contains("blue"))
            {
                return 2;
            }

            return name.Contains("reda") || name.Contains("red_lenses") || name.Contains("lensred") || name.Contains("redwarn") || name.Contains("flash_red") ? 1 : 0;
        }

        private void OnDestroy()
        {
            foreach (var material in _groupA)
            {
                Destroy(material);
            }

            foreach (var material in _groupB)
            {
                Destroy(material);
            }
        }
    }
}
