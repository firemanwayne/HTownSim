#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Blinkers, brake lamps and reversing lamps for a model whose lamps are modelled in its mesh. Lamp materials are found by name
    /// (Lamp_TurnL, Lamp_TurnR, Lamp_Brake, Lamp_Reverse anywhere in the material name); each gets one private emissive copy shared by
    /// all its renderers, so the asset materials and any parked copy of the model are never touched. Adds nothing to a model without
    /// such materials. Emission only: no extra lights (Bloom picks the lit lamps up).
    /// </summary>
    internal sealed class SignalLamps : MonoBehaviour
    {
        /// <summary>Material-name markers for each lamp kind.</summary>
        private const string TurnLeftMarker = "Lamp_TurnL";

        private const string TurnRightMarker = "Lamp_TurnR";

        private const string BrakeMarker = "Lamp_Brake";

        private const string ReverseMarker = "Lamp_Reverse";

        /// <summary>How fast the turn signals and hazards blink, in cycles per second. Raise it for a quicker blink; lower it for a slower one.</summary>
        private const float BlinkHertz = 1.5f;

        /// <summary>The share (0 to 1) of each blink cycle a lamp is lit. Raise it for longer on-time; lower it for brief blinks.</summary>
        private const float BlinkDuty = 0.5f;

        /// <summary>How bright (HDR emission, above the bloom threshold) a lit lamp glows. Raise it for a stronger glow and bloom; lower it for dull lamps.</summary>
        private const float LitEmission = 5f;

        private HTownSim.Runtime.Contracts.VehicleStateBase _response = null!;

        private readonly List<Lamp> _left = new();

        private readonly List<Lamp> _right = new();

        private readonly List<Lamp> _brake = new();

        private readonly List<Lamp> _reverse = new();

        private readonly List<Material> _copies = new();

        private float _phase;

        /// <summary>Number of lamp materials taken over, for verification.</summary>
        internal int LampCount => _left.Count + _right.Count + _brake.Count + _reverse.Count;

        /// <summary>Finds the lamp materials on the driven model; removes itself when there are none.</summary>
        internal void Initialize(HTownSim.Runtime.Contracts.VehicleStateBase response)
        {
            _response = response;

            var made = new Dictionary<Material, Material>();

            foreach (var renderer in GetComponentsInChildren<Renderer>(false))
            {
                var materials = renderer.sharedMaterials;

                var changed = false;

                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];

                    var list = source == null ? null : ListFor(source.name);

                    if (list is null)
                    {
                        continue;
                    }

                    if (!made.TryGetValue(source!, out var copy))
                    {
                        copy = new Material(source) { name = source!.name + "_Lamp" };

                        copy.EnableKeyword("_EMISSION");

                        copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

                        made[source] = copy;

                        _copies.Add(copy);

                        list.Add(new Lamp(copy, source.color));
                    }

                    materials[i] = copy;

                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }

            if (LampCount == 0)
            {
                Destroy(this);
            }
        }

        /// <summary>The lamp list a material belongs to, or null.</summary>
        private List<Lamp>? ListFor(string materialName)
        {
            if (materialName.Contains(TurnLeftMarker))
            {
                return _left;
            }

            if (materialName.Contains(TurnRightMarker))
            {
                return _right;
            }

            if (materialName.Contains(BrakeMarker))
            {
                return _brake;
            }

            return materialName.Contains(ReverseMarker) ? _reverse : null;
        }

        /// <summary>Blinks the turn lamps with the turn and hazard switches and lights brake and reverse lamps with the pedals.</summary>
        private void Update()
        {
            _phase = Mathf.Repeat(_phase + (BlinkHertz * Time.deltaTime), 1f);

            var blinkOn = _phase < BlinkDuty;

            Set(_left, _response.LeftSignalOn && blinkOn);

            Set(_right, _response.RightSignalOn && blinkOn);

            Set(_brake, !_response.IsPaused && _response.BrakeLightsOn);

            Set(_reverse, !_response.IsPaused && _response.ReverseEngaged);
        }

        private static void Set(List<Lamp> lamps, bool lit)
        {
            foreach (var lamp in lamps)
            {
                if (lamp.Lit == lit)
                {
                    continue;
                }

                lamp.Lit = lit;

                lamp.Copy.SetColor("_EmissionColor", lit ? lamp.Colour * LitEmission : Color.black);
            }
        }

        /// <summary>Releases the material copies.</summary>
        private void OnDestroy()
        {
            foreach (var copy in _copies)
            {
                Destroy(copy);
            }
        }

        /// <summary>One lamp material: its emissive copy, its colour and whether it is lit now.</summary>
        private sealed class Lamp
        {
            internal Lamp(Material copy, Color colour)
            {
                Copy = copy;

                Colour = colour;
            }

            internal Material Copy { get; }

            internal Color Colour { get; }

            internal bool Lit { get; set; }
        }
    }
}
