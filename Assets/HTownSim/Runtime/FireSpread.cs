#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>Grows the incident fire over time, ignites neighbouring bays along the facade and thickens the smoke. Owned by <see cref="FireResponse"/>.</summary>
    internal sealed class FireSpread : MonoBehaviour
    {
        /// <summary>Growth tuning: size multiplier at full burn and seconds to reach it from ignition.</summary>
        private const float MaxGrowth = 3f;
        private const float SecondsToFullGrowth = 75f;
        /// <summary>Flame emission in particles per second for the main seat of the fire and each satellite at full heat.</summary>
        private const float MainFlameRate = 65f;
        private const float SatelliteFlameRate = 45f;
        private const float SmokeRate = 15f;
        /// <summary>Particle caps sized for the intensified fire and smoke.</summary>
        private const int FlameParticleCap = 500;
        private const int SmokeParticleCap = 900;
        /// <summary>Lateral offsets of the satellite fires from the main seat, in metres; each ignites at the matching growth threshold.</summary>
        private static readonly float[] SatelliteOffsets = { 4f, -4f, 8f, -8f };
        private static readonly Color DenseSmoke = new Color(0.04f, 0.045f, 0.05f, 1f);

        private ParticleSystem _fire = null!;
        private ParticleSystem _smoke = null!;
        private ParticleSystem[] _satellites = new ParticleSystem[0];
        private Color _smokeColor;
        private float _flameRadius;
        private float _smokeRadius;
        private bool _ready;

        /// <summary>Current growth multiplier, 1 at ignition up to <see cref="MaxGrowth"/>.</summary>
        internal float Growth { get; private set; } = 1f;

        /// <summary>Number of satellite fires currently burning.</summary>
        internal int SpreadCount { get; private set; }

        /// <summary>Captures the authored fire and smoke settings and builds the satellite emitters as clones of the main flames.</summary>
        internal void Initialize(ParticleSystem fire, ParticleSystem smoke)
        {
            _fire = fire;

            _smoke = smoke;

            _flameRadius = fire.shape.radius;

            _smokeRadius = smoke.shape.radius;

            _smokeColor = smoke.main.startColor.color;

            var flameMain = fire.main;

            flameMain.maxParticles = FlameParticleCap;

            var smokeMain = smoke.main;

            smokeMain.maxParticles = SmokeParticleCap;

            _satellites = new ParticleSystem[SatelliteOffsets.Length];

            for (var i = 0; i < _satellites.Length; i++)
            {
                var clone = Instantiate(fire.gameObject, fire.transform.parent);

                clone.name = fire.name + "Spread" + i;

                _satellites[i] = clone.GetComponent<ParticleSystem>();

                _satellites[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            _ready = true;
        }

        /// <summary>Starts a fresh fire at the incident: growth resets, satellites go out and the main fire and smoke relight.</summary>
        internal void Ignite(Transform incident)
        {
            if (!_ready)
            {
                return;
            }

            Growth = 1f;

            SpreadCount = 0;

            for (var i = 0; i < _satellites.Length; i++)
            {
                _satellites[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                _satellites[i].transform.position = _fire.transform.position + incident.right * SatelliteOffsets[i];
            }

            Apply(1f);

            _fire.Play();

            _smoke.Play();
        }

        /// <summary>Advances growth unless water is on the fire, then refreshes the visuals. Call every frame while the fire burns.</summary>
        internal void Tick(float seconds, float heat, bool wetted)
        {
            if (!_ready || heat <= 0f)
            {
                return;
            }

            if (!wetted)
            {
                Growth = Mathf.Min(MaxGrowth, Growth + Mathf.Max(0f, seconds) * (MaxGrowth - 1f) / SecondsToFullGrowth);
            }

            Apply(heat);
        }

        /// <summary>Stops the flames, satellites and smoke when the fire is contained.</summary>
        internal void Extinguish()
        {
            _fire.Stop();

            _smoke.Stop();

            foreach (var satellite in _satellites)
            {
                satellite.Stop();
            }

            SpreadCount = 0;
        }

        /// <summary>Applies the growth and remaining heat to every emitter: more, bigger flames, extra fires and darker, denser, taller smoke.</summary>
        internal void Apply(float heat)
        {
            var growth01 = Mathf.InverseLerp(1f, MaxGrowth, Growth);

            ConfigureFlame(_fire, MainFlameRate * heat * (1f + growth01 * 1.2f), growth01);

            SpreadCount = 0;

            for (var i = 0; i < _satellites.Length; i++)
            {
                var ignited = heat > 0f && Growth >= 1f + (i + 1) * 0.4f;

                if (!ignited)
                {
                    continue;
                }

                SpreadCount++;

                ConfigureFlame(_satellites[i], SatelliteFlameRate * heat * (0.6f + 0.4f * growth01), growth01);

                if (!_satellites[i].isPlaying)
                {
                    _satellites[i].Play();
                }
            }

            var smokeMain = _smoke.main;

            smokeMain.startSizeMultiplier = 1f + growth01 * 0.7f;

            smokeMain.startSpeedMultiplier = 1f + growth01 * 0.6f;

            smokeMain.startColor = Color.Lerp(_smokeColor, DenseSmoke, growth01 * 0.85f);

            var smokeEmission = _smoke.emission;

            smokeEmission.rateOverTime = SmokeRate * (1f + growth01 * 3.5f) * Mathf.Lerp(0.5f, 1f, heat);

            var smokeShape = _smoke.shape;

            smokeShape.radius = _smokeRadius * (1f + growth01 * 0.8f);
        }

        /// <summary>Sets one flame emitter's rate, flame size and base footprint.</summary>
        private void ConfigureFlame(ParticleSystem flame, float rate, float growth01)
        {
            var emission = flame.emission;

            emission.rateOverTime = rate;

            var main = flame.main;

            main.startSizeMultiplier = 1f + growth01 * 0.9f;

            var shape = flame.shape;

            shape.radius = _flameRadius * (1f + growth01 * 0.8f);
        }
    }
}
