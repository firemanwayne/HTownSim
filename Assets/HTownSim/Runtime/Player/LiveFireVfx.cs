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
        /// <summary>Size of the fire at ignition, as a multiple of the authored fire. Raise it for bigger flames from the start; lower it for a small fire that has to grow.</summary>
        private const float MinScale = 1.1f;

        /// <summary>Size of the fire at full growth, as a multiple of the authored fire. Raise it for a bigger, more dramatic blaze when left alone; lower it to keep fires modest.</summary>
        private const float MaxScale = 2.6f;

        /// <summary>How much of the fire remains (as a fraction of its size) when the heat has been knocked down to zero: the embers. Raise it so a knocked-down fire still looks big; lower it so it shrinks to almost nothing.</summary>
        private const float EmbersScale = 0.35f;

        /// <summary>Smoke emission multiplier at ignition. Raise it for heavier smoke from the start; lower it for lighter smoke early on.</summary>
        private const float SmokeBoostMin = 3.2f;

        /// <summary>Smoke emission multiplier at full growth. Raise it for a thick black column; lower it for thinner smoke.</summary>
        private const float SmokeBoostMax = 8.5f;

        /// <summary>Flame emission multiplier at ignition. Raise it for fuller flames early; lower it for sparse ones.</summary>
        private const float FlameBoostMin = 0.9f;

        /// <summary>Flame emission multiplier at full growth. Raise it for denser flames; lower it for thinner ones.</summary>
        private const float FlameBoostMax = 1.7f;

        /// <summary>How long smoke particles live (a multiplier) at ignition. Raise it so smoke hangs longer and climbs higher; lower it for short-lived smoke.</summary>
        private const float SmokeLifeMin = 2.2f;

        /// <summary>How long smoke particles live (a multiplier) at full growth. Raise it for a taller, longer plume; lower it for a shorter one.</summary>
        private const float SmokeLifeMax = 3.6f;

        /// <summary>How fast smoke climbs (a multiplier) at ignition. Raise it for quicker rising smoke; lower it for lazier smoke.</summary>
        private const float SmokeRiseMin = 1.3f;

        /// <summary>How fast smoke climbs (a multiplier) at full growth. Raise it for a faster, taller column; lower it for a slow one.</summary>
        private const float SmokeRiseMax = 2.1f;

        /// <summary>How much the smoke spreads out as it rises (a multiplier) at ignition. Raise it for a wider plume; lower it for a narrow one.</summary>
        private const float SmokeWidenMin = 1.4f;

        /// <summary>How much the smoke spreads out as it rises (a multiplier) at full growth. Raise it for a broad cloud; lower it for a tight column.</summary>
        private const float SmokeWidenMax = 2.6f;

        /// <summary>Steady sideways push (metres per second) that carries the smoke downwind. Raise it to lean the plume over more; lower it (or 0) for smoke that rises straight up.</summary>
        private const float SmokeWind = 1.8f;

        /// <summary>The most smoke particles alive at once. Raise it so heavy smoke is not cut short (costs frame rate); lower it to save frame rate.</summary>
        private const int SmokeParticleCap = 9000;

        /// <summary>Distance (metres) within which the fire's crackle is full volume. Raise it so the crackle stays loud further out; lower it so it fades sooner.</summary>
        private const float AudioMinDistance = 25f;

        /// <summary>Distance (metres) beyond which the fire's crackle cannot be heard. Raise it so fires can be heard from blocks away; lower it to keep them local.</summary>
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
