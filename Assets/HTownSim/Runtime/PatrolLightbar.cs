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

        /// <summary>Full red-then-blue cycles per second.</summary>
        private const float CyclesPerSecond = 3f;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

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

                        _redOn = material.GetColor(EmissionColor);

                        material.EnableKeyword("_EMISSION");
                    }
                    else if (material.name.StartsWith(BlueMaterial))
                    {
                        _blue.Add(material);

                        _blueOn = material.GetColor(EmissionColor);

                        material.EnableKeyword("_EMISSION");
                    }
                }
            }

            _offset = Random.value;
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
        }
    }
}
