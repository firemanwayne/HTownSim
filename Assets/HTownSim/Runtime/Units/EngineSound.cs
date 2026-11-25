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

        /// <summary>Ground speed in metres per second at the top of each gear; the last entry is top speed (it should match the unit's top speed). Change an entry to move where the engine shifts: higher values keep a gear longer, lower values shift sooner. Adding or removing entries changes the number of gears.</summary>
        private static readonly float[] GearTop = { 4f, 7.5f, 12f, 17f, 22f };

        /// <summary>The overall volume (0 to 1) of the engine's idle and rev loops. Raise it to make the engine louder in the cab view; lower it to make it quieter. It does not change the road noise, turbo, squeal, air brake or alarm, which have their own volumes.</summary>
        private const float MasterVolume = 0.5f;
        /// <summary>The volume (0 to 1) of the road-noise bed at full road speed. Raise it for more tyre and wind noise at speed; lower it for a quieter ride.</summary>
        private const float RoadVolume = 0.3f;
        /// <summary>The ground speed (metres per second) at which the road noise reaches full volume and pitch. Raise it so the road noise builds more slowly with speed; lower it so it is full volume at lower speeds.</summary>
        private const float RoadFullSpeed = 20f;
        /// <summary>Engine speed (0 to 1) at idle. Raise it for a higher, busier idle; lower it for a slow, lumpy idle.</summary>
        private const float IdleRpm = 0.08f;
        /// <summary>Engine speed (0 to 1) while the aerial hydraulics work (outriggers or ladder moving). Raise it for a louder, higher-revving pump; lower it for a quieter one.</summary>
        private const float HydraulicRpm = 0.5f;
        /// <summary>How loaded (0 to 1) the engine sounds while the aerial hydraulics work. Raise it for a strained, heavier sound; lower it for a lighter one.</summary>
        private const float HydraulicLoad = 0.6f;
        /// <summary>How fast the revs climb when the throttle asks for more. Raise it for an eager, snappy engine; lower it for a heavy one that winds up slowly.</summary>
        private const float RpmRiseRate = 3.4f;
        /// <summary>How fast the revs fall when the throttle is lifted. Raise it so the revs drop quickly; lower it for revs that hang on.</summary>
        private const float RpmFallRate = 1.3f;
        /// <summary>How fast the revs fall while braking. Raise it so the engine settles at once when braking; lower it so the revs trail off.</summary>
        private const float BrakeFallRate = 3.2f;
        /// <summary>How much the revs rise with the throttle (0 to 1 of the rev range; doubled when standing still). Raise it for a bigger rev when you press the pedal; lower it for a more restrained one.</summary>
        private const float ThrottleBlip = 0.26f;
        /// <summary>How quickly the engine's load (the low-rev versus high-rev mix) follows the throttle. Raise it for a quick change of tone; lower it for a slow one.</summary>
        private const float LoadEase = 5f;
        /// <summary>A pitch multiplier on all the engine loops (1 is the recording's own pitch). Lower it for a deeper, heavier truck; raise it toward 1 or above for a lighter, higher engine.</summary>
        private const float HeavyPitch = 0.88f;

        private HTownSim.Runtime.Contracts.VehicleStateBase _response = null!;
        private AudioSource _idle = null!;
        private AudioSource _low = null!;
        private AudioSource _high = null!;
        private AudioSource _road = null!;
        private float _rpm = IdleRpm;
        private float _load;
        private AudioSource _turbo = null!;
        private AudioSource _squeal = null!;
        private AudioSource _airBrake = null!;
        private AudioSource _alarm = null!;
        private AudioClip _airBrakeClip = null!;
        private int _lastGear;
        private float _turboSpool;
        private float _boost;
        private float _previousSpeed;
        private bool _wasBraking;
        private float _peakSpeed;

        /// <summary>Peak volume (0 to 1) of the turbo whistle, heard while accelerating. Raise it for a more obvious turbo; lower it to make it fainter or inaudible.</summary>
        private const float TurboVolume = 0.07f;

        /// <summary>Turbo pitch at idle (1 is the clip's own pitch). Raise it for a higher turbo whine at low revs; lower it for a deeper one.</summary>
        private const float TurboPitchMin = 0.75f;

        /// <summary>Turbo pitch at full revs. Raise it for a shriller turbo at the top end; lower it to keep it low and rounded.</summary>
        private const float TurboPitchMax = 1.35f;

        /// <summary>How fast the turbo pitch follows the revs, per second. Raise it to track the revs tightly; lower it for a lazier, smoother glide.</summary>
        private const float TurboFollowRate = 6f;

        /// <summary>The acceleration (metres per second squared) above which the truck counts as accelerating and the turbo is heard. Raise it so only hard acceleration spools the turbo; lower it so even a gentle push does.</summary>
        private const float BoostAcceleration = 0.4f;

        /// <summary>How fast the turbo fades in when acceleration starts, per second. Raise it for an instant whistle; lower it for a gradual one.</summary>
        private const float BoostAttack = 6f;

        /// <summary>How fast the turbo fades out when acceleration stops, per second. Raise it for a quick cut-off; lower it for a whistle that trails.</summary>
        private const float BoostRelease = 8f;

        /// <summary>A gear drops back only below this fraction of the lower gear's top speed, so the engine does not hunt between gears. Raise it toward 1 for quicker downshifts; lower it for later ones.</summary>
        private const float DownshiftFraction = 0.85f;
        /// <summary>Peak volume (0 to 1) of the brake squeal at the end of a hard stop. Raise it for a louder squeal; lower it to quieten or remove it.</summary>
        private const float SquealVolume = 0.09f;

        /// <summary>The speed (metres per second) below which the brake squeal can be heard. Raise it to start the squeal earlier in a stop; lower it to squeal only at the very end.</summary>
        private const float SquealStartSpeed = 5f;

        /// <summary>The speed (metres per second) at which the squeal is loudest. Raise it to move the loudest part earlier in the stop; lower it to move it toward the end.</summary>
        private const float SquealPeakSpeed = 2.5f;

        /// <summary>The speed (metres per second) a stop must start from to squeal at all, so a tap of the brake stays silent. Raise it so only hard stops from higher speed squeal; lower it to squeal on gentler stops.</summary>
        private const float SquealMinPeak = 8f;
        /// <summary>Volume (0 to 1) of the air-brake hiss when the truck stops. Raise it for a louder hiss; lower it to quieten it. It is scaled by how fast the truck was going.</summary>
        private const float AirBrakeVolume = 0.7f;
        /// <summary>Volume (0 to 1) of the reversing alarm. Raise it for a louder beep; lower it to quieten it.</summary>
        private const float AlarmVolume = 0.9f;
        /// <summary>Distance (metres) within which the reversing alarm is at full volume. Raise it so the alarm stays loud further away; lower it so it fades sooner.</summary>
        private const float AlarmMinDistance = 6f;
        /// <summary>Distance (metres) beyond which the reversing alarm cannot be heard. Raise it so it carries further; lower it to keep it local.</summary>
        private const float AlarmMaxDistance = 90f;
        /// <summary>How far the revs drop at each gear change (0 to 1 of the rev range). Raise it for a bigger drop and a more obvious shift; lower it for smooth, hardly noticed shifts.</summary>
        private const float ShiftDrop = 0.16f;

        /// <summary>Creates the four looping sources and starts them silent.</summary>
        internal void Initialize(HTownSim.Runtime.Contracts.VehicleStateBase response)
        {
            _response = response;

            _idle = NewSource("EngineIdle", IdleResource);

            _low = NewSource("EngineLow", LowResource);

            _high = NewSource("EngineHigh", HighResource);

            _road = NewSource("EngineRoad", RoadResource);

            _turbo = NewSource("TurboWhine", null, TruckSounds.Turbo());

            _squeal = NewSource("BrakeSqueal", null, TruckSounds.Squeal());

            _airBrakeClip = TruckSounds.AirBrake();

            _airBrake = NewSource("AirBrake", null, _airBrakeClip, false);

            _alarm = NewSource("BackingAlarm", null, TruckSounds.BackingAlarm());

            // The backing alarm sits at the rear and is heard by anyone nearby, not only the driver.
            _alarm.transform.localPosition = new Vector3(0f, 1.2f, -4.5f);

            _alarm.spatialBlend = 1f;

            _alarm.rolloffMode = AudioRolloffMode.Linear;

            _alarm.minDistance = AlarmMinDistance;

            _alarm.maxDistance = AlarmMaxDistance;

            _alarm.dopplerLevel = 0f;
        }

        private AudioSource NewSource(string sourceName, string? resource, AudioClip? generated = null, bool loop = true)
        {
            var source = new GameObject(sourceName).AddComponent<AudioSource>();

            source.transform.SetParent(transform, false);

            source.clip = generated != null ? generated : Resources.Load<AudioClip>(resource!);

            source.loop = loop;

            source.playOnAwake = false;

            source.spatialBlend = 0f;

            source.volume = 0f;

            if (source.clip != null && loop)
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

            var gear = SelectGear(speed);

            if (gear > _lastGear && throttle > 0.3f && !braking)
            {
                // Upshift under power: revs drop, then climb again.
                _rpm = Mathf.Max(IdleRpm, _rpm - ShiftDrop);
            }

            _lastGear = gear;

            var low = gear == 0 ? 0f : GearTop[gear - 1];

            var inGear = Mathf.Clamp01((speed - low) / Mathf.Max(0.01f, GearTop[gear] - low));

            var target = Mathf.Lerp(IdleRpm, 0.9f, Mathf.Lerp(0.15f, 1f, inGear));

            target += Mathf.Abs(throttle) * ThrottleBlip * (speed < 1f ? 2f : 1f);

            target = Mathf.Clamp01(target);

            if (speed < 0.3f && Mathf.Abs(throttle) < 0.1f)
            {
                target = IdleRpm;
            }

            // The aerial's hydraulic pump runs off the engine: while the outriggers or ladder move, it revs up to working speed.
            var hydraulics = !paused && _response.AerialHydraulicsWorking;

            if (hydraulics)
            {
                target = Mathf.Max(target, HydraulicRpm);
            }

            var rate = target > _rpm ? RpmRiseRate : braking ? BrakeFallRate : RpmFallRate;

            _rpm = Mathf.MoveTowards(_rpm, target, rate * Time.deltaTime);

            var drive = Mathf.Abs(throttle) > 0.1f && !braking ? 1f : hydraulics ? HydraulicLoad : 0f;

            _load = Mathf.Lerp(_load, drive, 1f - Mathf.Exp(-LoadEase * Time.deltaTime));

            var rpm01 = Mathf.InverseLerp(IdleRpm, 1f, _rpm);

            var idleMix = 1f - Mathf.SmoothStep(0f, 0.3f, rpm01);

            var lowMix = Mathf.Clamp01(1f - Mathf.Abs(rpm01 - 0.38f) / 0.38f);

            var highMix = Mathf.SmoothStep(0.32f, 0.85f, rpm01);

            var master = paused ? 0f : MasterVolume * Mathf.Lerp(0.5f, 1.25f, _load);

            _idle.volume = master * idleMix;

            _low.volume = master * lowMix;

            _high.volume = master * highMix;

            _idle.pitch = HeavyPitch * (1f + rpm01 * 0.25f);

            _low.pitch = HeavyPitch * (0.8f + rpm01 * 0.55f);

            _high.pitch = HeavyPitch * (0.85f + rpm01 * 0.4f);

            _road.volume = paused ? 0f : RoadVolume * Mathf.Clamp01(speed / RoadFullSpeed);

            _road.pitch = 0.8f + Mathf.Clamp01(speed / RoadFullSpeed) * 0.5f;

            UpdateAccelerationAndBrakes(paused, speed, rpm01);
        }

        /// <summary>
        /// Gear for a ground speed with hysteresis: shifts up past a gear's top speed, but only drops back once speed is
        /// well under the lower gear's top, so cruising near a shift point no longer flips gears (and revs) back and forth.
        /// </summary>
        private int SelectGear(float speed)
        {
            var gear = Mathf.Clamp(_lastGear, 0, GearTop.Length - 1);

            while (gear < GearTop.Length - 1 && speed > GearTop[gear])
            {
                gear++;
            }

            while (gear > 0 && speed < GearTop[gear - 1] * DownshiftFraction)
            {
                gear--;
            }

            return gear;
        }

        /// <summary>Turbo whine under throttle, air-brake hiss and squeal when braking, and the backing alarm in reverse.</summary>
        private void UpdateAccelerationAndBrakes(bool paused, float speed, float rpm01)
        {
            // Turbo pitch tracks engine RPM (rising as the engine revs up, dropping at each upshift), lightly smoothed.
            _turboSpool = Mathf.Lerp(_turboSpool, rpm01, 1f - Mathf.Exp(-TurboFollowRate * Time.deltaTime));

            // Heard only while accelerating forward: throttle down, not braking and actually gaining speed. Cruising at top
            // speed, lifting off, decelerating and reversing are silent.
            var acceleration = Time.deltaTime > 0f ? (speed - _previousSpeed) / Time.deltaTime : 0f;

            _previousSpeed = speed;

            // Forward only: holding S to stop rolls straight into reverse, which should bring the backing alarm, not a whine.
            var accelerating = !paused && !_response.BrakeLightsOn && _response.ThrottleInput > 0.1f && _response.SignedSpeed > 0f && acceleration > BoostAcceleration;

            _boost = Mathf.MoveTowards(_boost, accelerating ? 1f : 0f, (accelerating ? BoostAttack : BoostRelease) * Time.deltaTime);

            _turbo.volume = paused ? 0f : TurboVolume * _boost * Mathf.Lerp(0.35f, 1f, _turboSpool);

            _turbo.pitch = Mathf.Lerp(TurboPitchMin, TurboPitchMax, _turboSpool);

            var braking = _response.BrakeLightsOn && !paused;

            _peakSpeed = braking ? Mathf.Max(_peakSpeed, speed) : speed;

            // Air brakes squeal briefly as a heavy truck comes to rest, not through the whole stop: fade in below
            // SquealStartSpeed, peak around SquealPeakSpeed, fade out to the stop.
            var squealShape = speed > SquealPeakSpeed
                ? Mathf.InverseLerp(SquealStartSpeed, SquealPeakSpeed, speed)
                : Mathf.InverseLerp(0.3f, SquealPeakSpeed, speed);

            _squeal.volume = braking && _peakSpeed >= SquealMinPeak ? SquealVolume * squealShape : 0f;

            _squeal.pitch = 0.95f + Mathf.Clamp01(speed / SquealStartSpeed) * 0.1f;

            if (braking && !_wasBraking && speed > 3f)
            {
                _airBrake.PlayOneShot(_airBrakeClip, AirBrakeVolume * Mathf.Clamp01(speed / 12f));
            }

            _wasBraking = braking;

            var beeping = _response.ReverseEngaged && !paused;

            _alarm.volume = beeping ? AlarmVolume : 0f;

            if (beeping && !_alarm.isPlaying)
            {
                _alarm.time = 0f;

                _alarm.Play();
            }
            else if (!beeping && _alarm.isPlaying)
            {
                _alarm.Stop();
            }
        }
    }
}
