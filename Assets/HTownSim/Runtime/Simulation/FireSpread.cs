#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>Grows the incident fire over time, ignites neighbouring bays along the facade and thickens the smoke. Owned by <see cref="FireResponse"/>.</summary>
    internal sealed class FireSpread : MonoBehaviour
    {
        /// <summary>The size multiplier at full burn. Raise it for fires that grow bigger if left alone (more flames and spread); lower it to cap how big a fire gets.</summary>
        private const float MaxGrowth = 3f;
        /// <summary>Seconds for an untended fire to reach full growth from ignition. Raise it for slow-growing fires and more time to respond; lower it for fires that blow up quickly.</summary>
        private const float SecondsToFullGrowth = 75f;
        /// <summary>Flame particles per second from the main seat of the fire at full heat (built-in flames, used when no authored fire is set). Raise it for denser flames; lower it for sparse ones.</summary>
        private const float MainFlameRate = 65f;
        /// <summary>Flame particles per second from each satellite fire at full heat (built-in flames). Raise it for fuller spreading fires; lower it for weaker ones.</summary>
        private const float SatelliteFlameRate = 45f;
        /// <summary>Smoke particles per second from the built-in smoke at ignition. Raise it for thicker smoke; lower it for thinner.</summary>
        private const float SmokeRate = 15f;
        /// <summary>The most built-in flame particles alive at once. Raise it so large fires are not cut short (costs frame rate); lower it to save frame rate.</summary>
        private const int FlameParticleCap = 500;

        /// <summary>Distance (metres) within which the fire's crackle is full volume. Raise it so the crackle stays loud further out; lower it so it fades sooner.</summary>
        private const float FireAudioMinDistance = 25f;

        /// <summary>Distance (metres) beyond which the fire's crackle cannot be heard. Raise it so fires can be heard from blocks away; lower it to keep them local.</summary>
        private const float FireAudioMaxDistance = 500f;
        /// <summary>The most built-in smoke particles alive at once. Raise it so heavy smoke is not cut short (costs frame rate); lower it to save frame rate.</summary>
        private const int SmokeParticleCap = 900;
        /// <summary>Lateral offsets of the satellite fires from the main seat, in metres; each ignites at the matching growth threshold.</summary>
        private static readonly float[] SatelliteOffsets = { 4f, -4f, 8f, -8f };
        private static readonly Color DenseSmoke = new Color(0.04f, 0.045f, 0.05f, 1f);

        /// <summary>Size of the fire at ignition, as a multiple of the authored fire. Raise it for bigger flames from the start; lower it for a small fire that has to grow.</summary>
        private const float VfxMinScale = 1.1f;
        /// <summary>Size of the fire at full growth, as a multiple of the authored fire. Raise it for a bigger, more dramatic blaze when left alone; lower it to keep fires modest.</summary>
        private const float VfxMaxScale = 2.6f;
        /// <summary>How much of the fire remains (as a fraction of its size) when the heat has been knocked down to zero: the embers. Raise it so a knocked-down fire still looks big; lower it so it shrinks to almost nothing.</summary>
        private const float VfxEmbersScale = 0.35f;
        /// <summary>The most smoke particles alive at once for the authored fire. Raise it so heavy smoke is not cut short (costs frame rate); lower it to save frame rate.</summary>
        private const float VfxSmokeCap = 9000;
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
        /// <summary>Smoke emission multiplier at ignition. Raise it for heavier smoke from the start; lower it for lighter smoke early on.</summary>
        private const float VfxSmokeBoostMin = 3.2f;
        /// <summary>Smoke emission multiplier at full growth. Raise it for a thick black column; lower it for thinner smoke.</summary>
        private const float VfxSmokeBoostMax = 8.5f;
        /// <summary>Flame emission multiplier at ignition. Raise it for fuller flames early; lower it for sparse ones.</summary>
        private const float VfxFlameBoostMin = 0.9f;
        /// <summary>Flame emission multiplier at full growth. Raise it for denser flames; lower it for thinner ones.</summary>
        private const float VfxFlameBoostMax = 1.7f;
        private static readonly string[] SmokeNames = { "Smoke", "Burning", "Dark" };

        private readonly Dictionary<ParticleSystem, float> _vfxRates = new Dictionary<ParticleSystem, float>();
        private readonly Dictionary<ParticleSystem, float> _vfxSizes = new Dictionary<ParticleSystem, float>();
        private readonly Dictionary<ParticleSystem, float> _vfxLifetimes = new Dictionary<ParticleSystem, float>();
        private readonly Dictionary<ParticleSystem, float> _vfxSpeeds = new Dictionary<ParticleSystem, float>();
        private readonly Dictionary<ParticleSystem, float> _vfxWidening = new Dictionary<ParticleSystem, float>();
        private GameObject? _vfxMain;
        private GameObject[] _vfxSatellites = new GameObject[0];
        private bool _useVfx;

        private ParticleSystem _fire = null!;
        private ParticleSystem _smoke = null!;
        private ParticleSystem[] _satellites = new ParticleSystem[0];
        private Color _smokeColor;
        private float _flameRadius;
        private float _smokeRadius;
        private bool _ready;

        /// <summary>Optional authored fire prefab for the main seat of the fire; when set it replaces the built-in flames and smoke.</summary>
        internal GameObject? MainPrefab { get; set; }

        /// <summary>Optional authored fire prefab for each fire that spreads along the facade.</summary>
        internal GameObject? SatellitePrefab { get; set; }

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

            if (MainPrefab != null)
            {
                SetupVfx(fire);
            }

            _ready = true;
        }

        /// <summary>Hides the built-in emitters and creates the authored main fire and spreading fires, all stopped.</summary>
        private void SetupVfx(ParticleSystem fire)
        {
            _useVfx = true;

            foreach (var system in new[] { _fire, _smoke })
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                system.GetComponent<ParticleSystemRenderer>().enabled = false;
            }

            _vfxMain = Instantiate(MainPrefab!, fire.transform.parent);

            _vfxMain.name = "FireVfxMain";

            Prepare(_vfxMain);

            var prefab = SatellitePrefab != null ? SatellitePrefab : MainPrefab!;

            _vfxSatellites = new GameObject[SatelliteOffsets.Length];

            for (var i = 0; i < _vfxSatellites.Length; i++)
            {
                _vfxSatellites[i] = Instantiate(prefab, fire.transform.parent);

                _vfxSatellites[i].name = "FireVfxSpread" + i;

                Prepare(_vfxSatellites[i]);
            }
        }

        /// <summary>Stops an authored fire, makes it scale with its transform, lifts the particle caps and remembers its base rates and sizes.</summary>
        private void Prepare(GameObject fireObject)
        {
            foreach (var system in fireObject.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;

                main.scalingMode = ParticleSystemScalingMode.Hierarchy;

                if (IsSmoke(system))
                {
                    main.maxParticles = (int)VfxSmokeCap;
                }

                _vfxRates[system] = system.emission.rateOverTimeMultiplier;

                _vfxSizes[system] = main.startSizeMultiplier;

                _vfxLifetimes[system] = main.startLifetimeMultiplier;

                _vfxSpeeds[system] = main.startSpeedMultiplier;

                _vfxWidening[system] = system.sizeOverLifetime.enabled ? system.sizeOverLifetime.sizeMultiplier : 1f;

                if (IsSmoke(system))
                {
                    var wind = system.forceOverLifetime;

                    wind.enabled = true;

                    wind.space = ParticleSystemSimulationSpace.World;

                    wind.x = new ParticleSystem.MinMaxCurve(SmokeWind);
                }

                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            foreach (var source in fireObject.GetComponentsInChildren<AudioSource>(true))
            {
                source.Stop();

                source.playOnAwake = false;

                source.loop = true;

                source.spatialBlend = 1f;

                source.rolloffMode = AudioRolloffMode.Logarithmic;

                source.minDistance = FireAudioMinDistance;

                source.maxDistance = FireAudioMaxDistance;

                source.dopplerLevel = 0f;
            }
        }

        /// <summary>Starts or stops the crackle of an authored fire, so it only sounds while that fire burns.</summary>
        private static void SetFireAudio(GameObject fireObject, bool on)
        {
            foreach (var source in fireObject.GetComponentsInChildren<AudioSource>(true))
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

            if (_useVfx)
            {
                foreach (var satellite in _vfxSatellites)
                {
                    satellite.GetComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                    SetFireAudio(satellite, false);
                }

                for (var i = 0; i < _vfxSatellites.Length; i++)
                {
                    _vfxSatellites[i].transform.position = _fire.transform.position + incident.right * SatelliteOffsets[i];
                }

                _vfxMain!.transform.position = _fire.transform.position;

                _vfxMain.GetComponent<ParticleSystem>().Clear(true);

                _vfxMain.GetComponent<ParticleSystem>().Play(true);

                SetFireAudio(_vfxMain, true);
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
            if (_useVfx)
            {
                _vfxMain!.GetComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmitting);

                SetFireAudio(_vfxMain, false);

                foreach (var satellite in _vfxSatellites)
                {
                    satellite.GetComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmitting);

                    SetFireAudio(satellite, false);
                }
            }

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

            if (_useVfx)
            {
                ApplyVfx(heat, growth01);

                return;
            }

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

        /// <summary>Scales the authored fires with growth and heat, ignites the spreading fires in turn, and keeps the smoke dense.</summary>
        private void ApplyVfx(float heat, float growth01)
        {
            _vfxMain!.transform.position = _fire.transform.position;

            Scale(_vfxMain, Mathf.Lerp(VfxMinScale, VfxMaxScale, growth01), heat, growth01);

            SpreadCount = 0;

            for (var i = 0; i < _vfxSatellites.Length; i++)
            {
                var ignited = heat > 0f && Growth >= 1f + (i + 1) * 0.4f;

                if (!ignited)
                {
                    continue;
                }

                SpreadCount++;

                var system = _vfxSatellites[i].GetComponent<ParticleSystem>();

                Scale(_vfxSatellites[i], Mathf.Lerp(0.7f, 1.3f, growth01), heat, growth01);

                if (!system.isPlaying)
                {
                    system.Play(true);
                }

                SetFireAudio(_vfxSatellites[i], true);
            }
        }

        /// <summary>Sizes one authored fire and sets every emitter's rate: smoke gets the large boost, flames a mild one, both fading with the heat.</summary>
        private void Scale(GameObject fireObject, float size, float heat, float growth01)
        {
            fireObject.transform.localScale = Vector3.one * size * Mathf.Lerp(VfxEmbersScale, 1f, heat);

            foreach (var source in fireObject.GetComponentsInChildren<AudioSource>(true))
            {
                source.volume = Mathf.Lerp(0.2f, 1f, heat) * Mathf.Lerp(0.85f, 1f, growth01);

                // Something on the authored prefab rescales the near distance as the fire grows, so hold it here.
                source.minDistance = FireAudioMinDistance;
            }

            var smokeBoost = Mathf.Lerp(VfxSmokeBoostMin, VfxSmokeBoostMax, growth01) * Mathf.Lerp(0.5f, 1f, heat);

            var flameBoost = Mathf.Lerp(VfxFlameBoostMin, VfxFlameBoostMax, growth01) * Mathf.Lerp(0.5f, 1f, heat);

            foreach (var system in fireObject.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!_vfxRates.TryGetValue(system, out var rate))
                {
                    continue;
                }

                var smoke = IsSmoke(system);

                var emission = system.emission;

                emission.rateOverTimeMultiplier = rate * (smoke ? smokeBoost : flameBoost);

                var main = system.main;

                main.startSizeMultiplier = _vfxSizes[system] * (smoke ? 1f + growth01 * 0.8f : 1f);

                if (!smoke)
                {
                    continue;
                }

                main.startLifetimeMultiplier = _vfxLifetimes[system] * Mathf.Lerp(SmokeLifeMin, SmokeLifeMax, growth01);

                main.startSpeedMultiplier = _vfxSpeeds[system] * Mathf.Lerp(SmokeRiseMin, SmokeRiseMax, growth01);

                var widening = system.sizeOverLifetime;

                if (widening.enabled)
                {
                    widening.sizeMultiplier = _vfxWidening[system] * Mathf.Lerp(SmokeWidenMin, SmokeWidenMax, growth01);
                }
            }
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
