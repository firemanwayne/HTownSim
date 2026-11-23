#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Diesel engine sound for the player's truck. Idle, 1000 RPM and 2000 RPM loops are crossfaded by a simulated engine
    /// speed that climbs through gears under throttle, lags on the way up, and falls away slowly when the driver lifts off
    /// or brakes, with a road-noise bed that follows ground speed.
    /// </summary>
    internal sealed class EngineSound : MonoBehaviour
    {
        private const string IdleResource = "TrafficAudio/Idle/VanIdle01";
        private const string RoadResource = "TrafficAudio/Drive/VanDrive01";
        private const string LowResource = "EngineAudio/Rpm1000";
        private const string HighResource = "EngineAudio/Rpm2000";

        /// <summary>Ground speed in metres per second at the top of each gear; the last entry is top speed.</summary>
        private static readonly float[] GearTop = { 3.2f, 5.8f, 8.6f, 11.2f, 14f };

        private const float MasterVolume = 0.42f;
        private const float RoadVolume = 0.3f;
        private const float RoadFullSpeed = 13f;
        private const float IdleRpm = 0.08f;
        private const float RpmRiseRate = 3.4f;
        private const float RpmFallRate = 1.3f;
        private const float BrakeFallRate = 3.2f;
        private const float ThrottleBlip = 0.18f;
        private const float LoadEase = 5f;
        private const float HeavyPitch = 0.88f;

        private FireResponse _response = null!;
        private AudioSource _idle = null!;
        private AudioSource _low = null!;
        private AudioSource _high = null!;
        private AudioSource _road = null!;
        private float _rpm = IdleRpm;
        private float _load;

        /// <summary>Creates the four looping sources and starts them silent.</summary>
        internal void Initialize(FireResponse response)
        {
            _response = response;

            _idle = NewSource("EngineIdle", IdleResource);

            _low = NewSource("EngineLow", LowResource);

            _high = NewSource("EngineHigh", HighResource);

            _road = NewSource("EngineRoad", RoadResource);
        }

        private AudioSource NewSource(string sourceName, string resource)
        {
            var source = new GameObject(sourceName).AddComponent<AudioSource>();

            source.transform.SetParent(transform, false);

            source.clip = Resources.Load<AudioClip>(resource);

            source.loop = true;

            source.playOnAwake = false;

            source.spatialBlend = 0f;

            source.volume = 0f;

            if (source.clip != null)
            {
                source.time = Random.value * source.clip.length;

                source.Play();
            }

            return source;
        }

        /// <summary>Advances the simulated engine speed and mixes the layers.</summary>
        private void Update()
        {
            if (_response == null)
            {
                return;
            }

            var paused = Time.timeScale <= 0f || _response.EngineMuted;

            var speed = Mathf.Abs(_response.SignedSpeed);

            var throttle = _response.ThrottleInput;

            var braking = _response.BrakeInput;

            var gear = 0;

            while (gear < GearTop.Length - 1 && speed > GearTop[gear])
            {
                gear++;
            }

            var low = gear == 0 ? 0f : GearTop[gear - 1];

            var inGear = Mathf.Clamp01((speed - low) / Mathf.Max(0.01f, GearTop[gear] - low));

            var target = Mathf.Lerp(IdleRpm, 0.9f, Mathf.Lerp(0.15f, 1f, inGear));

            target += Mathf.Abs(throttle) * ThrottleBlip * (speed < 1f ? 2f : 1f);

            target = Mathf.Clamp01(target);

            if (speed < 0.3f && Mathf.Abs(throttle) < 0.1f)
            {
                target = IdleRpm;
            }

            var rate = target > _rpm ? RpmRiseRate : braking ? BrakeFallRate : RpmFallRate;

            _rpm = Mathf.MoveTowards(_rpm, target, rate * Time.deltaTime);

            var drive = Mathf.Abs(throttle) > 0.1f && !braking ? 1f : 0f;

            _load = Mathf.Lerp(_load, drive, 1f - Mathf.Exp(-LoadEase * Time.deltaTime));

            var rpm01 = Mathf.InverseLerp(IdleRpm, 1f, _rpm);

            var idleMix = 1f - Mathf.SmoothStep(0f, 0.3f, rpm01);

            var lowMix = Mathf.Clamp01(1f - Mathf.Abs(rpm01 - 0.38f) / 0.38f);

            var highMix = Mathf.SmoothStep(0.32f, 0.85f, rpm01);

            var master = paused ? 0f : MasterVolume * Mathf.Lerp(0.55f, 1f, _load);

            _idle.volume = master * idleMix;

            _low.volume = master * lowMix;

            _high.volume = master * highMix;

            _idle.pitch = HeavyPitch * (1f + rpm01 * 0.25f);

            _low.pitch = HeavyPitch * (0.8f + rpm01 * 0.55f);

            _high.pitch = HeavyPitch * (0.85f + rpm01 * 0.4f);

            _road.volume = paused ? 0f : RoadVolume * Mathf.Clamp01(speed / RoadFullSpeed);

            _road.pitch = 0.8f + Mathf.Clamp01(speed / RoadFullSpeed) * 0.5f;
        }
    }
}
