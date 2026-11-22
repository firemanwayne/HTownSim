#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>Rolls the engine's wheels by the distance actually travelled and steers the front pair. Owned by <see cref="FireResponse"/>.</summary>
    internal sealed class WheelSpin : MonoBehaviour
    {
        /// <summary>Tyre radius in metres, from the wheel mesh profile.</summary>
        private const float WheelRadius = 0.65f;
        /// <summary>Distance between the front and rear axles in metres, used to turn yaw per metre into a steering angle.</summary>
        private const float Wheelbase = 5.05f;
        private const float MaxSteerDegrees = 35f;
        private const float SteerSmoothing = 12f;
        /// <summary>A move this long in one frame is a teleport (restart), not driving, so the wheels ignore it.</summary>
        private const float TeleportDistance = 5f;
        /// <summary>Below this travelled distance in a frame the steering angle cannot be measured and eases back to straight.</summary>
        private const float MinimumTravel = 0.002f;
        private const string FrontPrefix = "WheelFront";
        private const string RearPrefix = "WheelRear";

        private Transform[] _wheels = new Transform[0];
        private bool[] _isFront = new bool[0];
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
            var found = new System.Collections.Generic.List<Transform>();

            var front = new System.Collections.Generic.List<bool>();

            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                var isFront = child.name.StartsWith(FrontPrefix);

                if (isFront || child.name.StartsWith(RearPrefix))
                {
                    found.Add(child);

                    front.Add(isFront);
                }
            }

            _wheels = found.ToArray();

            _isFront = front.ToArray();

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

            _roll = Mathf.Repeat(_roll + travelled / WheelRadius * Mathf.Rad2Deg, 360f);

            var target = Mathf.Abs(travelled) > MinimumTravel
                ? Mathf.Clamp(Mathf.Atan(Wheelbase * yawDelta / travelled) * Mathf.Rad2Deg, -MaxSteerDegrees, MaxSteerDegrees)
                : 0f;

            _steer = Mathf.Lerp(_steer, target, 1f - Mathf.Exp(-SteerSmoothing * Time.deltaTime));

            Apply();
        }

        /// <summary>Writes the roll to all wheels and the steering yaw to the front pair.</summary>
        private void Apply()
        {
            for (var i = 0; i < _wheels.Length; i++)
            {
                _wheels[i].localRotation = Quaternion.Euler(0f, _isFront[i] ? _steer : 0f, 0f) * Quaternion.Euler(_roll, 0f, 0f);
            }
        }
    }
}
