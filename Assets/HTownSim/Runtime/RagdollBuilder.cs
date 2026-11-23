#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Builds a physics ragdoll on a humanoid model at the moment it is struck, and removes it again afterwards. Capsule
    /// colliders, rigidbodies and character joints are added to the main bones; nothing exists on a walking pedestrian.
    /// </summary>
    internal static class RagdollBuilder
    {
        private const float HeadRadius = 0.11f;
        private const float HeadLift = 0.08f;
        private const float Damping = 0.25f;
        private const float AngularDamping = 0.35f;
        private const float TwistLimit = 40f;
        private const float SwingLimit = 55f;
        private const float SpinRange = 4f;

        /// <summary>Bone, its parent bone, the bone its capsule reaches toward, capsule radius in metres and its share of the mass.</summary>
        private static readonly (HumanBodyBones Bone, HumanBodyBones Parent, HumanBodyBones Child, float Radius, float Share)[] Bones =
        {
            (HumanBodyBones.Hips, HumanBodyBones.Hips, HumanBodyBones.Spine, 0.13f, 14f),
            (HumanBodyBones.Spine, HumanBodyBones.Hips, HumanBodyBones.Head, 0.12f, 20f),
            (HumanBodyBones.Head, HumanBodyBones.Spine, HumanBodyBones.Head, HeadRadius, 5f),
            (HumanBodyBones.LeftUpperArm, HumanBodyBones.Spine, HumanBodyBones.LeftLowerArm, 0.05f, 3f),
            (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftHand, 0.045f, 2f),
            (HumanBodyBones.RightUpperArm, HumanBodyBones.Spine, HumanBodyBones.RightLowerArm, 0.05f, 3f),
            (HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightHand, 0.045f, 2f),
            (HumanBodyBones.LeftUpperLeg, HumanBodyBones.Hips, HumanBodyBones.LeftLowerLeg, 0.08f, 10f),
            (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftFoot, 0.06f, 5f),
            (HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips, HumanBodyBones.RightLowerLeg, 0.08f, 10f),
            (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightFoot, 0.06f, 5f),
        };

        /// <summary>Adds rigidbodies, colliders and joints to the model's bones and launches them with the requested velocity.</summary>
        internal static void Build(RagdollRequest request)
        {
            var animator = request.Animator;

            var total = 0f;

            foreach (var entry in Bones)
            {
                total += entry.Share;
            }

            var bodies = new Dictionary<HumanBodyBones, Rigidbody>();

            foreach (var entry in Bones)
            {
                var bone = animator.GetBoneTransform(entry.Bone);

                if (bone == null)
                {
                    continue;
                }

                var scale = Mathf.Max(0.0001f, bone.lossyScale.x);

                var collider = entry.Bone == HumanBodyBones.Head
                    ? (Collider)AddHeadCollider(bone, entry.Radius / scale)
                    : AddCapsule(bone, animator.GetBoneTransform(entry.Child), entry.Radius / scale);

                if (request.Ignore != null)
                {
                    Physics.IgnoreCollision(collider, request.Ignore, true);
                }

                var body = bone.gameObject.AddComponent<Rigidbody>();

                body.mass = request.Mass * entry.Share / total;

                body.linearDamping = Damping;

                body.angularDamping = AngularDamping;

                body.linearVelocity = request.Velocity;

                body.angularVelocity = Random.insideUnitSphere * SpinRange;

                bodies[entry.Bone] = body;

                if (entry.Parent != entry.Bone && bodies.TryGetValue(entry.Parent, out var parent))
                {
                    AddJoint(bone, parent);
                }
            }
        }

        /// <summary>Removes the joints, rigidbodies and colliders that <see cref="Build"/> added.</summary>
        internal static void Remove(Animator animator)
        {
            foreach (var entry in Bones)
            {
                var bone = animator.GetBoneTransform(entry.Bone);

                if (bone == null)
                {
                    continue;
                }

                Destroy(bone.GetComponent<CharacterJoint>());

                Destroy(bone.GetComponent<Rigidbody>());

                Destroy(bone.GetComponent<Collider>());
            }
        }

        private static void Destroy(Object? component)
        {
            if (component != null)
            {
                Object.Destroy(component);
            }
        }

        /// <summary>A capsule along the bone toward its child, in the bone's own units.</summary>
        private static CapsuleCollider AddCapsule(Transform bone, Transform? child, float radius)
        {
            var capsule = bone.gameObject.AddComponent<CapsuleCollider>();

            var local = child != null ? bone.InverseTransformPoint(child.position) : Vector3.up * radius * 3f;

            var axis = 0;

            if (Mathf.Abs(local.y) > Mathf.Abs(local[axis]))
            {
                axis = 1;
            }

            if (Mathf.Abs(local.z) > Mathf.Abs(local[axis]))
            {
                axis = 2;
            }

            capsule.direction = axis;

            capsule.radius = radius;

            capsule.center = local / 2f;

            capsule.height = Mathf.Max(radius * 2f, local.magnitude + radius);

            return capsule;
        }

        private static SphereCollider AddHeadCollider(Transform bone, float radius)
        {
            var sphere = bone.gameObject.AddComponent<SphereCollider>();

            sphere.radius = radius;

            sphere.center = bone.InverseTransformPoint(bone.position + Vector3.up * HeadLift);

            return sphere;
        }

        private static void AddJoint(Transform bone, Rigidbody parent)
        {
            var joint = bone.gameObject.AddComponent<CharacterJoint>();

            joint.connectedBody = parent;

            joint.enableProjection = true;

            joint.lowTwistLimit = new SoftJointLimit { limit = -TwistLimit };

            joint.highTwistLimit = new SoftJointLimit { limit = TwistLimit };

            joint.swing1Limit = new SoftJointLimit { limit = SwingLimit };

            joint.swing2Limit = new SoftJointLimit { limit = SwingLimit };
        }
    }
}
