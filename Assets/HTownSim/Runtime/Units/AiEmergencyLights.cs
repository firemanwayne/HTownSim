#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The warning lights and the siren of a computer-driven unit. It finds the unit's lens materials by name (the models name them
    /// differently: Engine07Clean_LensRed, Amb07_LensRedWarn, L07_Red_lenses, DC08_Flash_RedA ...), sorts them into two groups
    /// that flash in turn, and gives the unit its own copies so no other unit of the same model is touched. While on, the lenses
    /// glow bright enough for the bloom, and a wail loops from the unit.
    /// </summary>
    internal sealed class AiEmergencyLights : MonoBehaviour
    {
        /// <summary>Alternations per second of a computer-driven unit's warning lights. Raise it for faster flashing; lower it for slower.</summary>
        private const float FlashHertz = 3f;

        /// <summary>How bright a lit lens glows (HDR emission, so the bloom picks it up). Raise it for a stronger glow; lower it for a dull one.</summary>
        private const float FlashEmission = 6f;

        /// <summary>Volume (0 to 1) of a computer-driven unit's siren at its source. Raise it to make responding units louder; lower it to quieten them.</summary>
        private const float SirenVolume = 0.6f;

        /// <summary>Distance (metres) within which a responding unit's siren is at full volume. Raise it to keep it loud further out; lower it so it fades sooner.</summary>
        private const float SirenMinimumDistance = 12f;

        /// <summary>Distance (metres) beyond which a responding unit's siren cannot be heard. Raise it so sirens carry further across the city; lower it to keep them local.</summary>
        private const float SirenMaximumDistance = 220f;

        /// <summary>The wail: length, sample rate and the pitch sweep (Hz).</summary>
        private const int SampleRate = 22050;

        private const int SirenSeconds = 4;

        /// <summary>The low end of the wail's pitch, in Hz. Raise it for a higher siren; lower it for a deeper one.</summary>
        private const float LowPitch = 620f;

        /// <summary>How far (Hz) the wail rises above its low pitch. Raise it for a wider swing between low and high; lower it for a flatter tone.</summary>
        private const float PitchSweep = 220f;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private static AudioClip? _wail;

        private readonly List<Material> _groupA = new List<Material>();

        private readonly List<Material> _groupB = new List<Material>();

        private readonly List<Color> _colourA = new List<Color>();

        private readonly List<Color> _colourB = new List<Color>();

        private AudioSource? _siren;

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

            if (_siren == null && siren)
            {
                _siren = gameObject.AddComponent<AudioSource>();

                _siren.clip = Wail();

                _siren.loop = true;

                _siren.spatialBlend = 1f;

                _siren.rolloffMode = AudioRolloffMode.Linear;

                _siren.minDistance = SirenMinimumDistance;

                _siren.maxDistance = SirenMaximumDistance;

                _siren.volume = SirenVolume;
            }

            if (_siren != null)
            {
                if (siren && !_siren.isPlaying)
                {
                    _siren.Play();
                }
                else if (!siren && _siren.isPlaying)
                {
                    _siren.Stop();
                }
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

        /// <summary>A rising and falling wail, made once and shared.</summary>
        private static AudioClip Wail()
        {
            if (_wail != null)
            {
                return _wail;
            }

            var samples = new float[SampleRate * SirenSeconds];

            var phase = 0f;

            for (var i = 0; i < samples.Length; i++)
            {
                var time = i / (float)SampleRate;

                phase += 2f * Mathf.PI * (LowPitch + (PitchSweep * Mathf.Sin(time * Mathf.PI * 2f / SirenSeconds * 0.5f))) / SampleRate;

                samples[i] = Mathf.Sin(phase) * 0.2f;
            }

            _wail = AudioClip.Create("AiWail", samples.Length, 1, SampleRate, false);

            _wail.SetData(samples, 0);

            return _wail;
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
