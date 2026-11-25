#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The patient at an EMS call: one of the city's pedestrian models, standing where the call is and then collapsing as a
    /// ragdoll so it lies on the ground in a natural heap. The body is frozen once it has settled (so it cannot jitter or be
    /// pushed) and removed some seconds after the call is cleared.
    /// </summary>
    internal sealed class DownedCitizen : MonoBehaviour
    {
        /// <summary>The body's mass in kilograms, shared across the ragdoll bones.</summary>
        private const float BodyMass = 75f;

        /// <summary>How long the body is left to fall and settle before it is frozen in place.</summary>
        private const float SettleSeconds = 5f;

        /// <summary>The probe for the ground starts this far above the call and reaches this far below it.</summary>
        private const float ProbeUp = 2.5f;

        private const float ProbeDown = 8f;

        /// <summary>The body starts this far above the ground so no bone begins inside it.</summary>
        private const float DropHeight = 0.12f;

        private GameObject? _model;

        private Animator? _animator;

        private float _freezeAt;

        private float _removeAt;

        /// <summary>Creates the (empty) patient holder in the scene.</summary>
        internal static DownedCitizen Create()
        {
            return new GameObject(nameof(DownedCitizen)).AddComponent<DownedCitizen>();
        }

        /// <summary>True while a patient is lying there.</summary>
        internal bool HasPatient => _model != null;

        /// <summary>Puts a patient at the spot; false when the scene has no pedestrian models to use.</summary>
        internal bool Show(PatientRequest request)
        {
            Clear();

            var models = FindAnyObjectByType<TrafficSystem>()?.PedestrianModels;

            if (models is null || models.Length == 0)
            {
                return false;
            }

            var model = Instantiate(models[Mathf.Abs(request.Variant) % models.Length], transform);

            model.name = "Patient";

            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
            {
                DestroyImmediate(behaviour);
            }

            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
            {
                DestroyImmediate(collider);
            }

            var animator = model.GetComponentInChildren<Animator>();

            if (animator == null || animator.avatar == null)
            {
                Destroy(model);

                return false;
            }

            animator.runtimeAnimatorController = null;

            animator.applyRootMotion = false;

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var facing = new Vector3(request.Facing.x, 0f, request.Facing.z);

            model.transform.SetPositionAndRotation(
                GroundBelow(request.Position) + (Vector3.up * DropHeight),
                facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing.normalized) : Quaternion.identity);

            _model = model;

            _animator = animator;

            RagdollBuilder.Build(new RagdollRequest { Animator = animator, Mass = BodyMass, Ignore = request.Ignore, Velocity = Vector3.zero });

            _freezeAt = Time.time + SettleSeconds;

            _removeAt = 0f;

            return true;
        }

        /// <summary>Removes the patient after the delay (immediately when zero), e.g. once the call is cleared.</summary>
        internal void Hide(float delaySeconds)
        {
            if (_model == null)
            {
                return;
            }

            if (delaySeconds <= 0f)
            {
                Clear();

                return;
            }

            _removeAt = Time.time + delaySeconds;
        }

        /// <summary>Freezes the settled body and removes it when its time comes.</summary>
        private void Update()
        {
            if (_model == null)
            {
                return;
            }

            if (_freezeAt > 0f && Time.time >= _freezeAt)
            {
                _freezeAt = 0f;

                foreach (var body in _model.GetComponentsInChildren<Rigidbody>())
                {
                    body.isKinematic = true;
                }
            }

            if (_removeAt > 0f && Time.time >= _removeAt)
            {
                Clear();
            }
        }

        /// <summary>Takes the ragdoll off and destroys the model.</summary>
        private void Clear()
        {
            if (_animator != null)
            {
                RagdollBuilder.Remove(_animator);
            }

            if (_model != null)
            {
                Destroy(_model);
            }

            _model = null;

            _animator = null;

            _freezeAt = 0f;

            _removeAt = 0f;
        }

        /// <summary>The ground under a point: the first solid surface found by a short probe from just above it, else the point itself.</summary>
        private static Vector3 GroundBelow(Vector3 point)
        {
            var origin = point + (Vector3.up * ProbeUp);

            return Physics.Raycast(origin, Vector3.down, out var hit, ProbeUp + ProbeDown, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point
                : point;
        }
    }
}
