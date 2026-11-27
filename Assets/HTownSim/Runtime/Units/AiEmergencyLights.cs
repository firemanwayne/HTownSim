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
        /// <summary>How this kind of unit flashes (rate, pulses, pulse length): the same definition the player's truck of this kind uses, set by <see cref="Configure"/>.</summary>
        private UnitBehavior _behavior = UnitBehavior.Engine;

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

        /// <summary>True while the siren is meant to sound (a spectator mirrors it from the server).</summary>
        internal bool SirenOn { get; private set; }

        private void Awake()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (TryAddBarLens(renderer))
                {
                    continue;
                }

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

        /// <summary>Gives the lights and siren the flash pattern, siren tones and volume of the unit's kind.</summary>
        internal void Configure(UnitBehavior behavior)
        {
            _behavior = behavior;

            if (_siren != null)
            {
                _siren.Configure(behavior);
            }
        }

        /// <summary>Turns the flashing and the siren on or off.</summary>
        internal void Set(bool on, bool siren)
        {
            _on = on;

            SirenOn = siren;

            if (!on)
            {
                Apply(_groupA, _colourA, false);

                Apply(_groupB, _colourB, false);

                foreach (var lens in _bar)
                {
                    lens.Material.SetColor(EmissionColor, Color.black);
                }
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

            // The player's pattern: group A in the first half of the cycle, group B in the second, each pulsing twice in its half.
            var phase = Mathf.Repeat((Time.time * _behavior.FlashHertz) + _offset, 1f);

            var half = phase < 0.5f ? phase * 2f : (phase - 0.5f) * 2f;

            var pulse = FlashPattern.IsLit(half, _behavior.FlashPulses, _behavior.FlashPulseDuty);

            Apply(_groupA, _colourA, phase < 0.5f && pulse);

            Apply(_groupB, _colourB, phase >= 0.5f && pulse);

            UpdateBar();
        }

        /// <summary>One lens of the roof light bar: its column, whether it is a red column's lower lens (which trails the upper one), and its own material.</summary>
        private readonly struct BarLens
        {
            internal BarLens(Material material, Color lit, int column, float delay)
            {
                Material = material;

                Lit = lit;

                Column = column;

                Delay = delay;
            }

            internal Material Material { get; }

            internal Color Lit { get; }

            internal int Column { get; }

            internal float Delay { get; }
        }

        private readonly List<BarLens> _bar = new List<BarLens>();

        /// <summary>The same bar layout the player's engine uses (see <c>ApparatusLights</c>): 48 lenses named EmergencyLighting_Lens0..47, 12 columns of 4.</summary>
        private const string BarMeshPrefix = "EmergencyLighting_Lens";

        private const int BarLenses = 48;

        private const int BarColumns = 12;

        private const int LensesPerColumn = 4;

        /// <summary>The fraction of a cycle each column starts after the previous one, the lower lens of a red column's trail, the lit share of a cycle and the spread of the columns' own rates: the player's values.</summary>
        private const float ColumnStagger = 0.173f;

        private const float RollDelay = 0.13f;

        private const float BarDuty = 0.45f;

        private const float BarSpread = 0.07f;

        private const float SpreadStep = 0.6180339f;

        /// <summary>If the renderer is a roof-bar lens, gives it its own red or white lens material and joins it to the bar. Returns true when it was one.</summary>
        private bool TryAddBarLens(Renderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();

            if (filter == null || filter.sharedMesh == null)
            {
                return false;
            }

            var meshName = filter.sharedMesh.name;

            if (!meshName.StartsWith(BarMeshPrefix) || !int.TryParse(meshName.Substring(BarMeshPrefix.Length), out var number) || number < 0 || number >= BarLenses)
            {
                return false;
            }

            var column = number / LensesPerColumn;

            var upper = number % LensesPerColumn >= 2;

            var red = Mathf.Min(column, BarColumns - 1 - column) % 2 == 0;

            var colour = red ? new Color(1f, 0.08f, 0.05f) : new Color(0.9f, 0.95f, 1f);

            var material = renderer.material;

            material.EnableKeyword("_EMISSION");

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", colour * 0.35f);
            }

            material.SetColor(EmissionColor, Color.black);

            _bar.Add(new BarLens(material, colour * FlashEmission, column, red && !upper ? RollDelay : 0f));

            return true;
        }

        /// <summary>Flashes the bar's columns each at their own phase and rate, as the player's bar does.</summary>
        private void UpdateBar()
        {
            foreach (var lens in _bar)
            {
                var hertz = _behavior.FrontRearHertz * (1f + (BarSpread * ((Mathf.Repeat(lens.Column * SpreadStep, 1f) * 2f) - 1f)));

                var phase = Mathf.Repeat((Time.time * hertz) + (lens.Column * ColumnStagger) + _offset - lens.Delay, 1f);

                lens.Material.SetColor(EmissionColor, phase < BarDuty ? lens.Lit : Color.black);
            }
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

            foreach (var lens in _bar)
            {
                Destroy(lens.Material);
            }
        }
    }
}
