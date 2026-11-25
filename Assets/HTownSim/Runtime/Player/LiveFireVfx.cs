#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// One authored fire (the same flames, smoke and crackle prefab the player's incident uses) at a place in the city: it grows while nobody
    /// is working it, and shrinks to embers as the heat is knocked down. Owned by <see cref="LiveIncidentFires"/>.
    /// </summary>
    internal sealed class LiveFireVfx
    {
        /// <summary>Size of the fire at ignition and at full growth, and how little of it remains when the heat is gone.</summary>
        private const float MinScale = 1.1f;

        private const float MaxScale = 2.6f;

        private const float EmbersScale = 0.35f;

        /// <summary>Smoke is heavy: its emission multiplier at ignition and at full growth; the flames get a milder one.</summary>
        private const float SmokeBoostMin = 3.2f;

        private const float SmokeBoostMax = 8.5f;

        private const float FlameBoostMin = 0.9f;

        private const float FlameBoostMax = 1.7f;

        private const float SmokeLifeMin = 2.2f;

        private const float SmokeLifeMax = 3.6f;

        private const float SmokeRiseMin = 1.3f;

        private const float SmokeRiseMax = 2.1f;

        private const float SmokeWidenMin = 1.4f;

        private const float SmokeWidenMax = 2.6f;

        private const float SmokeWind = 1.8f;

        private const int SmokeParticleCap = 9000;

        /// <summary>The crackle is full volume within this distance and silent past the maximum (metres).</summary>
        private const float AudioMinDistance = 25f;

        private const float AudioMaxDistance = 500f;

        private static readonly string[] SmokeNames = { "Smoke", "Burning", "Dark" };

        private readonly Dictionary<ParticleSystem, float> _rates = new Dictionary<ParticleSystem, float>();

        private readonly Dictionary<ParticleSystem, float> _sizes = new Dictionary<ParticleSystem, float>();

        private readonly Dictionary<ParticleSystem, float> _lifetimes = new Dictionary<ParticleSystem, float>();

        private readonly Dictionary<ParticleSystem, float> _speeds = new Dictionary<ParticleSystem, float>();

        private readonly Dictionary<ParticleSystem, float> _widening = new Dictionary<ParticleSystem, float>();

        private readonly GameObject _fire;

        /// <summary>Creates the fire at a place from the authored prefab and lights it.</summary>
        internal LiveFireVfx(GameObject prefab, Vector3 position, Transform parent)
        {
            _fire = Object.Instantiate(prefab, position, Quaternion.identity, parent);

            _fire.name = "LiveFire";

            Prepare();

            Set(1f, 0f);

            _fire.GetComponent<ParticleSystem>().Play(true);

            SetAudio(true);
        }

        /// <summary>The fire's object (null once destroyed).</summary>
        internal GameObject? Object_ => _fire;

        /// <summary>Sizes the fire from its growth (0 at ignition to 1 at full growth) and its remaining heat (1 full to 0 out).</summary>
        internal void Set(float heat, float growth01)
        {
            if (_fire == null)
            {
                return;
            }

            _fire.transform.localScale = Vector3.one * Mathf.Lerp(MinScale, MaxScale, growth01) * Mathf.Lerp(EmbersScale, 1f, heat);

            foreach (var source in _fire.GetComponentsInChildren<AudioSource>(true))
            {
                source.volume = Mathf.Lerp(0.2f, 1f, heat) * Mathf.Lerp(0.85f, 1f, growth01);

                source.minDistance = AudioMinDistance;
            }

            var smokeBoost = Mathf.Lerp(SmokeBoostMin, SmokeBoostMax, growth01) * Mathf.Lerp(0.5f, 1f, heat);

            var flameBoost = Mathf.Lerp(FlameBoostMin, FlameBoostMax, growth01) * Mathf.Lerp(0.5f, 1f, heat);

            foreach (var system in _fire.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!_rates.TryGetValue(system, out var rate))
                {
                    continue;
                }

                var smoke = IsSmoke(system);

                var emission = system.emission;

                emission.rateOverTimeMultiplier = rate * (smoke ? smokeBoost : flameBoost);

                var main = system.main;

                main.startSizeMultiplier = _sizes[system] * (smoke ? 1f + (growth01 * 0.8f) : 1f);

                if (!smoke)
                {
                    continue;
                }

                main.startLifetimeMultiplier = _lifetimes[system] * Mathf.Lerp(SmokeLifeMin, SmokeLifeMax, growth01);

                main.startSpeedMultiplier = _speeds[system] * Mathf.Lerp(SmokeRiseMin, SmokeRiseMax, growth01);

                var widening = system.sizeOverLifetime;

                if (widening.enabled)
                {
                    widening.sizeMultiplier = _widening[system] * Mathf.Lerp(SmokeWidenMin, SmokeWidenMax, growth01);
                }
            }
        }

        /// <summary>Stops the flames, smoke and crackle (what is already in the air fades out).</summary>
        internal void Stop()
        {
            if (_fire == null)
            {
                return;
            }

            _fire.GetComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmitting);

            SetAudio(false);
        }

        /// <summary>Removes the fire.</summary>
        internal void Destroy()
        {
            if (_fire != null)
            {
                Object.Destroy(_fire);
            }
        }

        private void Prepare()
        {
            foreach (var system in _fire.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;

                main.scalingMode = ParticleSystemScalingMode.Hierarchy;

                if (IsSmoke(system))
                {
                    main.maxParticles = SmokeParticleCap;

                    var wind = system.forceOverLifetime;

                    wind.enabled = true;

                    wind.space = ParticleSystemSimulationSpace.World;

                    wind.x = new ParticleSystem.MinMaxCurve(SmokeWind);
                }

                _rates[system] = system.emission.rateOverTimeMultiplier;

                _sizes[system] = main.startSizeMultiplier;

                _lifetimes[system] = main.startLifetimeMultiplier;

                _speeds[system] = main.startSpeedMultiplier;

                _widening[system] = system.sizeOverLifetime.enabled ? system.sizeOverLifetime.sizeMultiplier : 1f;

                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            foreach (var source in _fire.GetComponentsInChildren<AudioSource>(true))
            {
                source.Stop();

                source.playOnAwake = false;

                source.loop = true;

                source.spatialBlend = 1f;

                source.rolloffMode = AudioRolloffMode.Logarithmic;

                source.minDistance = AudioMinDistance;

                source.maxDistance = AudioMaxDistance;

                source.dopplerLevel = 0f;
            }
        }

        private void SetAudio(bool on)
        {
            foreach (var source in _fire.GetComponentsInChildren<AudioSource>(true))
            {
                if (on && !source.isPlaying)
                {
                    source.Play();
                }
                else if (!on && source.isPlaying)
                {
                    source.Stop();
                }
            }
        }

        private static bool IsSmoke(ParticleSystem system)
        {
            foreach (var word in SmokeNames)
            {
                if (system.name.Contains(word))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
