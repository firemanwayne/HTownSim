#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Rolls the wheels of a computer-driven unit by the distance it actually travels. It finds the wheel parts by name (the models
    /// call them WheelFront, L07_wheel_0_, DC08_WheelRear ...), takes each wheel's radius from its size, and turns it about the
    /// unit's right-hand axis, so a unit that moves always has turning wheels and a parked one has still wheels.
    /// </summary>
    internal sealed class AiWheelSpin : MonoBehaviour
    {
        private const string WheelWord = "wheel";

        /// <summary>A wheel radius is never taken as less than this (metres), so a tiny measured size cannot spin the wheels absurdly fast. Raise it to slow the spin of small wheels; lower it to allow small wheels.</summary>
        private const float MinimumRadius = 0.25f;

        /// <summary>The radius (metres) used when a wheel's size cannot be measured. Raise it for slower spin; lower it for faster.</summary>
        private const float DefaultRadius = 0.5f;

        /// <summary>A move this long (metres) in one frame is a jump (the unit was parked again), not driving, so the wheels ignore it. Raise or lower it as for the player's WheelSpin.</summary>
        private const float JumpDistance = 5f;

        private readonly List<Transform> _wheels = new List<Transform>();

        private readonly List<Quaternion> _base = new List<Quaternion>();

        private readonly List<Vector3> _axis = new List<Vector3>();

        private readonly List<float> _radius = new List<float>();

        private readonly List<float> _roll = new List<float>();

        private Vector3 _last;

        /// <summary>How many wheels were found.</summary>
        internal int WheelCount => _wheels.Count;

        private void Start()
        {
            foreach (var child in GetComponentsInChildren<Transform>(false))
            {
                if (child == transform || !child.name.ToLowerInvariant().Contains(WheelWord) || HasWheelAncestor(child))
                {
                    continue;
                }

                _wheels.Add(child);

                _base.Add(child.localRotation);

                // Roll about the unit's right-hand axis, written in the wheel's parent space (imported pivots can be turned).
                _axis.Add(child.parent.InverseTransformDirection(transform.right).normalized);

                _radius.Add(RadiusOf(child));

                _roll.Add(0f);
            }

            _last = transform.position;
        }

        private void LateUpdate()
        {
            var offset = transform.position - _last;

            _last = transform.position;

            if (offset.magnitude > JumpDistance)
            {
                return;
            }

            var travelled = Vector3.Dot(offset, transform.forward);

            for (var i = 0; i < _wheels.Count; i++)
            {
                _roll[i] = Mathf.Repeat(_roll[i] + (travelled / _radius[i] * Mathf.Rad2Deg), 360f);

                _wheels[i].localRotation = Quaternion.AngleAxis(_roll[i], _axis[i]) * _base[i];
            }
        }

        /// <summary>True when a part above this one (up to the unit) is already a wheel, so only the outer part rolls.</summary>
        private bool HasWheelAncestor(Transform part)
        {
            for (var parent = part.parent; parent != null && parent != transform; parent = parent.parent)
            {
                if (parent.name.ToLowerInvariant().Contains(WheelWord))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Half the height of the wheel's parts, which is the tyre radius.</summary>
        private static float RadiusOf(Transform wheel)
        {
            var renderers = wheel.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                return DefaultRadius;
            }

            var bounds = renderers[0].bounds;

            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            return Mathf.Max(MinimumRadius, bounds.extents.y);
        }
    }
}
