#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Rolls the driven apparatus's wheels by the distance actually travelled and steers the front axle. Wheel names,
    /// tyre radius and wheelbase come from its <see cref="ApparatusProfile"/>. Owned by <see cref="HTownSim.Runtime.Contracts.VehicleStateBase"/>.
    /// </summary>
    internal sealed class WheelSpin : MonoBehaviour
    {
        private const float MaxSteerDegrees = 35f;
        private const float SteerSmoothing = 12f;
        /// <summary>A move this long in one frame is a teleport (restart), not driving, so the wheels ignore it.</summary>
        private const float TeleportDistance = 5f;
        /// <summary>Below this travelled distance in a frame the steering angle cannot be measured and eases back to straight.</summary>
        private const float MinimumTravel = 0.002f;

        private float _wheelRadius;
        private float _wheelbase;
        private Transform[] _wheels = new Transform[0];
        private bool[] _isFront = new bool[0];
        /// <summary>Each wheel's rotation at start (imported wheels may carry one); roll and steer are applied on top.</summary>
        private Quaternion[] _baseRotation = new Quaternion[0];
        /// <summary>Per wheel, the truck's right (roll) and up (steer) axes in the wheel's parent space.</summary>
        private Vector3[] _rollAxis = new Vector3[0];
        private Vector3[] _steerAxis = new Vector3[0];
        private Vector3 _lastPosition;
        private float _lastYaw;
        private float _roll;
        private float _steer;

        /// <summary>Number of wheels found, for verification.</summary>
        internal int WheelCount => _wheels.Length;

        /// <summary>Current roll angle in degrees, for verification.</summary>
        internal float Roll => _roll;

        /// <summary>Current front steering angle in degrees, for verification.</summary>
        internal float Steer => _steer;

        /// <summary>Finds the wheel pivots by name and records the starting pose.</summary>
        private void Start()
        {
            var profile = ApparatusProfile.For(gameObject);

            _wheelRadius = profile.WheelRadius;

            _wheelbase = profile.Wheelbase;

            var found = new System.Collections.Generic.List<Transform>();

            var front = new System.Collections.Generic.List<bool>();

            // Only wheels on active parts: a switched-off visual's wheels are left alone.
            foreach (var child in GetComponentsInChildren<Transform>(false))
            {
                var isFront = StartsWithAny(child.name, profile.FrontWheelPrefixes);

                if (isFront || StartsWithAny(child.name, profile.RearWheelPrefixes))
                {
                    found.Add(child);

                    front.Add(isFront);
                }
            }

            _wheels = found.ToArray();

            _isFront = front.ToArray();

            _baseRotation = new Quaternion[_wheels.Length];

            _rollAxis = new Vector3[_wheels.Length];

            _steerAxis = new Vector3[_wheels.Length];

            for (var i = 0; i < _wheels.Length; i++)
            {
                _baseRotation[i] = _wheels[i].localRotation;

                // Roll about the truck's right axis and steer about its up axis, expressed in each wheel's parent space
                // (imported rigs can have pivots whose local axes are not the truck's).
                var parent = _wheels[i].parent;

                _rollAxis[i] = parent.InverseTransformDirection(transform.right).normalized;

                _steerAxis[i] = parent.InverseTransformDirection(transform.up).normalized;
            }

            _lastPosition = transform.position;

            _lastYaw = transform.eulerAngles.y;
        }

        /// <summary>Rolls every wheel by distance over radius and yaws the front wheels to match the turn rate.</summary>
        private void LateUpdate()
        {
            var offset = transform.position - _lastPosition;

            var yaw = transform.eulerAngles.y;

            var travelled = Vector3.Dot(offset, transform.forward);

            var yawDelta = Mathf.DeltaAngle(_lastYaw, yaw) * Mathf.Deg2Rad;

            _lastPosition = transform.position;

            _lastYaw = yaw;

            if (offset.magnitude > TeleportDistance)
            {
                _roll = 0f;

                _steer = 0f;

                Apply();

                return;
            }

            _roll = Mathf.Repeat(_roll + travelled / _wheelRadius * Mathf.Rad2Deg, 360f);

            var target = Mathf.Abs(travelled) > MinimumTravel
                ? Mathf.Clamp(Mathf.Atan(_wheelbase * yawDelta / travelled) * Mathf.Rad2Deg, -MaxSteerDegrees, MaxSteerDegrees)
                : 0f;

            _steer = Mathf.Lerp(_steer, target, 1f - Mathf.Exp(-SteerSmoothing * Time.deltaTime));

            Apply();
        }

        /// <summary>Writes the roll to all wheels and the steering yaw to the front pair, both in the parent's axes.</summary>
        private void Apply()
        {
            for (var i = 0; i < _wheels.Length; i++)
            {
                _wheels[i].localRotation = Quaternion.AngleAxis(_isFront[i] ? _steer : 0f, _steerAxis[i]) * Quaternion.AngleAxis(_roll, _rollAxis[i]) * _baseRotation[i];
            }
        }

        private static bool StartsWithAny(string name, string[] prefixes)
        {
            foreach (var prefix in prefixes)
            {
                if (name.StartsWith(prefix))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
