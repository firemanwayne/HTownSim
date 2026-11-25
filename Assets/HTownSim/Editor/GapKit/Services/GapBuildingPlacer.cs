#nullable enable
using System.Collections.Generic;
using System.Linq;
using HTownSim.EditorTools.GapKit.Models;
using UnityEditor;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>Puts a planned building into the scene and clears the trees and props that would stand inside the new buildings.</summary>
    internal static class GapBuildingPlacer
    {
        /// <summary>The buildings' base: the lots' top (BuildRealMidtown's LotTop).</summary>
        internal const float LotTop = 0.215f;

        /// <summary>Creates one building's object: its mesh and materials, a box collider, at its place and angle under <paramref name="group"/>.</summary>
        internal static void Place(Transform group, GapPlan plan, Mesh mesh, string[] keys)
        {
            var go = new GameObject("B" + plan.Parcel.Index, typeof(MeshFilter), typeof(MeshRenderer));

            go.transform.SetParent(group, false);

            go.transform.localPosition = new Vector3(plan.Centre.x, LotTop, plan.Centre.y);

            go.transform.localRotation = Quaternion.Euler(0f, plan.Yaw, 0f);

            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            go.GetComponent<MeshRenderer>().sharedMaterials = keys.Select(GapMaterials.Load).ToArray();

            var box = go.AddComponent<BoxCollider>();

            box.center = new Vector3(0f, plan.Height / 2f, 0f);

            box.size = new Vector3(plan.Width, plan.Height, plan.Depth);

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
        }

        /// <summary>Hides the children of a group (trees, props) that stand inside or within <paramref name="clearance"/> metres of a planned building; returns how many.</summary>
        internal static int HideInside(Transform? group, IReadOnlyList<GapPlan> plans, float clearance)
        {
            if (group == null)
            {
                return 0;
            }

            var hidden = 0;

            foreach (Transform item in group)
            {
                var point = new Vector2(item.position.x, item.position.z);

                if (plans.Any(plan => Inside(plan, point, clearance)))
                {
                    item.gameObject.SetActive(false);

                    hidden++;
                }
            }

            return hidden;
        }

        private static bool Inside(GapPlan plan, Vector2 point, float clearance)
        {
            var offset = point - plan.Centre;

            var forward = new Vector2(Mathf.Sin(plan.Yaw * Mathf.Deg2Rad), Mathf.Cos(plan.Yaw * Mathf.Deg2Rad));

            var right = new Vector2(forward.y, -forward.x);

            return Mathf.Abs(Vector2.Dot(offset, right)) <= (plan.Width / 2f) + clearance && Mathf.Abs(Vector2.Dot(offset, forward)) <= (plan.Depth / 2f) + clearance;
        }
    }
}
