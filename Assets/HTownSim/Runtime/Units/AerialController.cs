#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Works the aerial on a rigged ladder truck (Ladder 07's exported rig: CTRL_Turntable, CTRL_Aerial_Elevation,
    /// CTRL_Aerial_Fly_1-3, CTRL_Outrigger_*, the outrigger beams and jack sleeves, and the hydraulic rams).
    ///
    /// Every motion has its own hold-to-move pair of keys:
    ///   4 / 5   outriggers extend / retract (the beams slide out)
    ///   6 / 7   outriggers lower / raise (the jacks go down to the ground)
    ///   Up / Down        ladder raise / lower
    ///   ] / [            ladder extend / retract (the fly sections)
    ///   Left / Right     rotate the turntable
    /// Interlocks: the ladder will not raise unless the outriggers are at least 75% out and 75% down; the outriggers
    /// cannot retract or come up while the ladder is off its bed; the ladder rotates and extends only once it is clear
    /// of the cab, and it lowers into the bed only when rotated home and fully retracted. The truck cannot drive until
    /// everything is stowed, and water flows from the ladder tip while the ladder is up.
    ///
    /// Each part moves along the truck's own axes expressed in its parent's space, captured in the stowed pose, so it
    /// works whatever local axes the imported pivots have. The motions follow the Blender rig's drivers.
    /// </summary>
    internal sealed class AerialController : MonoBehaviour
    {
        /// <summary>Rig values (from the Blender drivers): outrigger reach and drop, stowed beam width, fly travel, tip reach.</summary>
        private const float OutriggerReach = 1.15f;

        private const float OutriggerDrop = 0.2475f;

        private const float BeamRestWidth = 0.24f;

        private const float FlyTravel = 7.5f;

        private const float TipReach = 10.355f;

        /// <summary>Hydraulic ram piston: length is the base-to-anchor distance less the barrel's share, never below the minimum.</summary>
        private const float RamBarrel = 0.35f;

        private const float RamMinimum = 0.05f;

        /// <summary>Operating speeds: seconds for a full outrigger extension and jack stroke, degrees and fraction per second.</summary>
        private const float OutriggerSeconds = 3f;

        private const float JackSeconds = 2f;

        private const float ElevationSpeed = 9f;

        private const float SlewSpeed = 18f;

        private const float ExtensionRate = 0.12f;

        private const float MaximumElevation = 80f;

        /// <summary>The ladder may not raise until the outriggers are at least this far out and down.</summary>
        internal const float OutriggerInterlock = 0.75f;

        /// <summary>Below this angle the ladder is over the cab: no rotating or extending, and it beds only when home.</summary>
        private const float CabClearance = 12f;

        /// <summary>How close to straight ahead the turntable must be to lower the ladder into its bed.</summary>
        private const float HomeTolerance = 1f;

        private const float Settled = 0.001f;

        /// <summary>
        /// Outrigger warning sound (Resources/AerialAudio/OutriggerErch: a 115 ms buzzy "erch" made from the supplied
        /// censor beep at its original 1 kHz pitch, driven, fluttered and band-passed, plus 20 ms of silence, so it
        /// repeats about 7 times a second), looped from each outrigger pad while the outriggers are moving.
        /// </summary>
        private const string OutriggerBeepResource = "AerialAudio/OutriggerErch";

        private const float BeepVolume = 0.45f;

        private const float BeepMinDistance = 4f;

        private const float BeepMaxDistance = 50f;

        /// <summary>Low-pass cutoff (Hz) that slightly muffles the beep; lower is more muffled.</summary>
        private const float BeepMuffleCutoff = 2500f;

        private readonly List<AudioSource> _beeps = new();

        private HTownSim.Runtime.Contracts.VehicleStateBase _response = null!;

        private float _reach;

        private float _down;

        private float _elevation;

        private float _azimuth;

        private float _extension;

        private Pivot _turntable = null!;

        private Vector3 _turntableUp;

        private Pivot _elevationPivot = null!;

        private Vector3 _elevationAxis;

        private readonly List<Slider> _flies = new();

        private readonly List<Slider> _outriggers = new();

        private readonly List<Slider> _sleeves = new();

        private readonly List<Beam> _beams = new();

        private readonly List<Ram> _rams = new();

        private Transform _tipSection = null!;

        private Vector3 _tipDirection;

        /// <summary>True while everything is stowed (outriggers in and up, ladder bedded and retracted), so the truck may drive.</summary>
        internal bool Stowed => _reach <= Settled && _down <= Settled && _elevation <= Settled && _extension <= Settled;

        /// <summary>True while the ladder is off its bed.</summary>
        internal bool LadderUp => _elevation > Settled;

        /// <summary>True when the outriggers are set far enough (out and down) for the ladder to raise.</summary>
        internal bool OutriggersSet => _reach >= OutriggerInterlock && _down >= OutriggerInterlock;

        /// <summary>Current values, for the HUD and verification: outrigger reach and jack stroke 0-1, elevation and rotation in degrees, extension 0-1.</summary>
        internal float OutriggerOut => _reach;

        internal float OutriggerDown => _down;

        internal float Elevation => _elevation;

        internal float Azimuth => _azimuth;

        internal float Extension => _extension;

        /// <summary>True while anything moved in the last step (outriggers, ladder raise, rotation or extension): the hydraulics are working.</summary>
        internal bool Working { get; private set; }

        /// <summary>True while the outrigger warning beep is sounding, for verification.</summary>
        internal bool BeepSounding => _beeps.Count > 0 && _beeps[0].isPlaying;

        /// <summary>The outrigger beep sources (one on each pad), for verification.</summary>
        internal IReadOnlyList<AudioSource> BeepSources => _beeps;

        /// <summary>Why the last requested motion was refused (empty when nothing was), for the HUD.</summary>
        internal string Notice { get; private set; } = "";

        /// <summary>The ladder tip in world space (where the waterway discharges).</summary>
        internal Vector3 TipWorld => _tipSection.position + _tipSection.TransformDirection(_tipDirection) * TipReach;

        /// <summary>One line for the HUD: outriggers, ladder, and any refused motion.</summary>
        internal string StatusLine => $"OUTRIGGERS {_reach * 100f:0}% OUT {_down * 100f:0}% DOWN   LADDER {_elevation:0}°  ROT {_azimuth:0}°  EXT {_extension * 100f:0}%"
            + (Notice.Length > 0 ? "   " + Notice : "");

        /// <summary>Adds the controller if the driven apparatus has an aerial rig; returns null otherwise.</summary>
        internal static AerialController? Create(HTownSim.Runtime.Contracts.VehicleStateBase response)
        {
            var turntable = Find(response.transform, "CTRL_Turntable");

            if (turntable == null)
            {
                return null;
            }

            var controller = response.gameObject.AddComponent<AerialController>();

            controller.Initialize(response, turntable);

            return controller;
        }

        /// <summary>Captures every moving part's stowed pose and its motion axes.</summary>
        private void Initialize(HTownSim.Runtime.Contracts.VehicleStateBase response, Transform turntable)
        {
            _response = response;

            var truck = response.transform;

            _turntable = new Pivot(turntable);

            _turntableUp = turntable.parent.InverseTransformDirection(truck.up).normalized;

            var elevation = Find(turntable, "CTRL_Aerial_Elevation")!;

            _elevationPivot = new Pivot(elevation);

            _elevationAxis = elevation.parent.InverseTransformDirection(truck.right).normalized;

            for (var i = 1; i <= 3; i++)
            {
                var fly = Find(elevation, "CTRL_Aerial_Fly_" + i)!;

                _flies.Add(new Slider(fly, fly.parent.InverseTransformDirection(truck.forward).normalized, Vector3.zero));

                _tipSection = fly;
            }

            _tipDirection = _tipSection.InverseTransformDirection(truck.forward).normalized;

            foreach (var part in truck.GetComponentsInChildren<Transform>(true))
            {
                if (part.parent == null)
                {
                    continue;
                }

                var side = Mathf.Sign(truck.InverseTransformPoint(part.position).x);

                var outward = part.parent.InverseTransformDirection(truck.right * side).normalized;

                var down = part.parent.InverseTransformDirection(-truck.up).normalized;

                if (part.name.StartsWith("CTRL_Outrigger_"))
                {
                    _outriggers.Add(new Slider(part, outward, down));

                    AddBeep(part);
                }
                else if (part.name.StartsWith("L07_JackSleeve_"))
                {
                    _sleeves.Add(new Slider(part, outward, Vector3.zero));
                }
                else if (part.name.StartsWith("L07_OutriggerBeam_"))
                {
                    _beams.Add(new Beam(part, outward, LargestAxis(part.InverseTransformDirection(truck.right))));
                }
                else if (part.name.StartsWith("Hydraulic_base_"))
                {
                    var anchor = Find(truck, "Hydraulic_lift_anchor_" + part.name.Substring("Hydraulic_base_".Length));

                    if (anchor != null)
                    {
                        _rams.Add(new Ram(part, anchor));
                    }
                }
            }
        }

        /// <summary>Reads the operator's keys (each motion has its own pair) and moves the rig.</summary>
        private void Update()
        {
            var keyboard = Keyboard.current;

            if (keyboard == null || _response.IsPaused)
            {
                SetBeep(false);

                Working = false;

                return;
            }

            float Pair(bool positive, bool negative)
            {
                return (positive ? 1f : 0f) - (negative ? 1f : 0f);
            }

            Operate(new AerialInput(
                Pair(keyboard.digit4Key.isPressed, keyboard.digit5Key.isPressed),
                Pair(keyboard.digit6Key.isPressed, keyboard.digit7Key.isPressed),
                Pair(keyboard.upArrowKey.isPressed, keyboard.downArrowKey.isPressed),
                Pair(keyboard.rightBracketKey.isPressed, keyboard.leftBracketKey.isPressed),
                Pair(keyboard.rightArrowKey.isPressed, keyboard.leftArrowKey.isPressed)),
                Time.deltaTime);
        }

        /// <summary>
        /// Applies one step of operator input (each -1..1) with the interlocks, then poses the rig. Separate from the
        /// keyboard so it can be driven and verified directly.
        /// </summary>
        internal void Operate(AerialInput input, float dt)
        {
            var notice = "";

            var before = (_reach, _down, _elevation, _azimuth, _extension);

            // Outriggers: they may go out and down freely, but not in or up while the ladder is off its bed.
            var lockOutriggers = LadderUp;

            var outriggersRefused = lockOutriggers && (input.OutriggerOut < 0f || input.OutriggerDown < 0f);

            if (outriggersRefused)
            {
                notice = "LOWER THE LADDER FIRST";
            }

            var reachBefore = _reach;

            var downBefore = _down;

            if (!outriggersRefused)
            {
                _reach = Mathf.Clamp01(_reach + input.OutriggerOut * dt / OutriggerSeconds);

                _down = Mathf.Clamp01(_down + input.OutriggerDown * dt / JackSeconds);
            }

            // The warning beep repeats for as long as an outrigger is actually moving, and stops when the control stops.
            SetBeep(!Mathf.Approximately(_reach, reachBefore) || !Mathf.Approximately(_down, downBefore));

            // Ladder raise/lower: raising needs the outriggers set; it beds (below cab clearance) only when home and retracted.
            if (input.Raise > 0f && !OutriggersSet)
            {
                notice = $"SET OUTRIGGERS {OutriggerInterlock * 100f:0}% OUT AND DOWN";
            }
            else if (input.Raise != 0f)
            {
                // Not home or not retracted: it may not go below the cab clearance (or lower than it already is).
                var floor = Mathf.Abs(_azimuth) > HomeTolerance || _extension > Settled ? Mathf.Min(CabClearance, _elevation) : 0f;

                if (input.Raise < 0f && floor > 0f && _elevation <= floor + 0.01f)
                {
                    notice = "ROTATE HOME AND RETRACT TO BED THE LADDER";
                }

                _elevation = Mathf.Clamp(_elevation + input.Raise * ElevationSpeed * dt, floor, MaximumElevation);
            }

            // Rotation and extension only once the ladder is clear of the cab.
            var clear = _elevation >= CabClearance;

            if (!clear && (input.Slew != 0f || input.Extend > 0f))
            {
                notice = "RAISE THE LADDER CLEAR OF THE CAB FIRST";
            }

            if (clear)
            {
                _azimuth = Mathf.Repeat(_azimuth + input.Slew * SlewSpeed * dt + 180f, 360f) - 180f;
            }

            if (clear || input.Extend < 0f)
            {
                _extension = Mathf.Clamp01(_extension + input.Extend * ExtensionRate * dt);
            }

            Notice = notice;

            Working = before != (_reach, _down, _elevation, _azimuth, _extension);

            Apply();
        }

        /// <summary>Snaps everything back to stowed (restart).</summary>
        internal void ResetStowed()
        {
            _reach = 0f;

            _down = 0f;

            _elevation = 0f;

            _azimuth = 0f;

            _extension = 0f;

            Notice = "";

            Working = false;

            SetBeep(false);

            Apply();
        }

        /// <summary>Puts a looping beep source on an outrigger pad, so the warning comes from the outrigger and moves with it.</summary>
        private void AddBeep(Transform pad)
        {
            var clip = Resources.Load<AudioClip>(OutriggerBeepResource);

            if (clip == null)
            {
                return;
            }

            var emitter = new GameObject("OutriggerBeep");

            emitter.transform.SetParent(pad, false);

            var source = emitter.AddComponent<AudioSource>();

            source.clip = clip;

            source.loop = true;

            source.playOnAwake = false;

            source.volume = BeepVolume;

            source.spatialBlend = 1f;

            source.rolloffMode = AudioRolloffMode.Linear;

            source.minDistance = BeepMinDistance;

            source.maxDistance = BeepMaxDistance;

            // Slightly muffled, as if from inside the outrigger housing: roll off the highs.
            var muffle = emitter.AddComponent<AudioLowPassFilter>();

            muffle.cutoffFrequency = BeepMuffleCutoff;

            muffle.lowpassResonanceQ = 1f;

            _beeps.Add(source);
        }

        /// <summary>Starts the outrigger beeps together (so they stay in step), or stops them at once.</summary>
        private void SetBeep(bool sounding)
        {
            if (_beeps.Count == 0 || _beeps[0].isPlaying == sounding)
            {
                return;
            }

            foreach (var source in _beeps)
            {
                if (sounding)
                {
                    source.Play();
                }
                else
                {
                    source.Stop();
                }
            }
        }

        /// <summary>Poses the rig from the current values (as the Blender drivers do).</summary>
        private void Apply()
        {
            _turntable.Target.localRotation = Quaternion.AngleAxis(_azimuth, _turntableUp) * _turntable.Rotation;

            // A positive turn about the truck's right axis tips the ladder down in Unity, so raising is negative.
            _elevationPivot.Target.localRotation = Quaternion.AngleAxis(-_elevation, _elevationAxis) * _elevationPivot.Rotation;

            foreach (var fly in _flies)
            {
                fly.Target.localPosition = fly.Position + fly.Out * (FlyTravel * _extension);
            }

            // Pads follow both the beam (out) and the jack (down); the sleeves and beams only go out.
            foreach (var outrigger in _outriggers)
            {
                outrigger.Target.localPosition = outrigger.Position + outrigger.Out * (OutriggerReach * _reach) + outrigger.Down * (OutriggerDrop * _down);
            }

            foreach (var sleeve in _sleeves)
            {
                sleeve.Target.localPosition = sleeve.Position + sleeve.Out * (OutriggerReach * _reach);
            }

            foreach (var beam in _beams)
            {
                // The beam spans from the body to the pad: it moves out half the reach and stretches by the full reach.
                beam.Target.localPosition = beam.Position + beam.Out * (OutriggerReach * 0.5f * _reach);

                var scale = beam.Scale;

                scale[beam.Axis] *= 1f + OutriggerReach * _reach / BeamRestWidth;

                beam.Target.localScale = scale;
            }

            foreach (var ram in _rams)
            {
                ram.Apply();
            }
        }

        private static Transform? Find(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static int LargestAxis(Vector3 v)
        {
            var a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

            return a.x >= a.y && a.x >= a.z ? 0 : a.y >= a.z ? 1 : 2;
        }

        /// <summary>A rotating part and its stowed rotation.</summary>
        private sealed class Pivot
        {
            internal Pivot(Transform target)
            {
                Target = target;

                Rotation = target.localRotation;
            }

            internal Transform Target { get; }

            internal Quaternion Rotation { get; }
        }

        /// <summary>A sliding part: its stowed position and the directions it moves (in its parent's space).</summary>
        private sealed class Slider
        {
            internal Slider(Transform target, Vector3 outward, Vector3 down)
            {
                Target = target;

                Position = target.localPosition;

                Out = outward;

                Down = down;
            }

            internal Transform Target { get; }

            internal Vector3 Position { get; }

            internal Vector3 Out { get; }

            internal Vector3 Down { get; }
        }

        /// <summary>An outrigger beam: slides out and stretches along one of its own axes.</summary>
        private sealed class Beam
        {
            internal Beam(Transform target, Vector3 outward, int axis)
            {
                Target = target;

                Position = target.localPosition;

                Scale = target.localScale;

                Out = outward;

                Axis = axis;
            }

            internal Transform Target { get; }

            internal Vector3 Position { get; }

            internal Vector3 Scale { get; }

            internal Vector3 Out { get; }

            internal int Axis { get; }
        }

        /// <summary>
        /// A hydraulic ram: the base aims at its anchor on the ladder (the rig's damped track) and each piston stretches
        /// with the distance between them (the rig's piston drivers).
        /// </summary>
        private sealed class Ram
        {
            private readonly Transform _base;

            private readonly Transform _anchor;

            private readonly Quaternion _baseRotation;

            private readonly Vector3 _track;

            private readonly float _restLength;

            private readonly List<(Transform Piston, Vector3 Position, Vector3 Scale, int Axis)> _pistons = new();

            internal Ram(Transform rigBase, Transform anchor)
            {
                _base = rigBase;

                _anchor = anchor;

                _baseRotation = rigBase.localRotation;

                _track = rigBase.InverseTransformDirection(anchor.position - rigBase.position).normalized;

                _restLength = PistonLength(Vector3.Distance(rigBase.position, anchor.position));

                var trackWorld = rigBase.TransformDirection(_track);

                foreach (Transform child in rigBase)
                {
                    if (child.name.StartsWith("L07_HydraulicPiston_"))
                    {
                        _pistons.Add((child, child.localPosition, child.localScale, LargestAxis(child.InverseTransformDirection(trackWorld))));
                    }
                }
            }

            internal void Apply()
            {
                // Aim: turn the base so its stowed track direction points at the anchor again.
                _base.localRotation = _baseRotation;

                var current = _base.TransformDirection(_track);

                var wanted = _anchor.position - _base.position;

                _base.rotation = Quaternion.FromToRotation(current, wanted) * _base.rotation;

                var length = PistonLength(wanted.magnitude);

                foreach (var (piston, position, scale, axis) in _pistons)
                {
                    // Piston centre sits RamBarrel + half its length along the track; its length scales with the distance.
                    piston.localPosition = position + _track * ((length - _restLength) / 2f);

                    var s = scale;

                    s[axis] *= length / _restLength;

                    piston.localScale = s;
                }
            }

            private static float PistonLength(float distance)
            {
                return Mathf.Max(RamMinimum, distance - RamBarrel);
            }
        }
    }
}
