#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HTownSim.EditorTools.GapKit.Models;
using HTownSim.EditorTools.GapKit.Services;
using UnityEditor;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit
{
    /// <summary>
    /// Checks the whole map against the City lot outlines, in world space (so the rotated Station 16 neighborhood is compared like the rest):
    /// which placed objects (street trees, props, lamps, parked cars) stand where there is no lot or sidewalk, which buildings stand outside
    /// their lot or spill out of it, and which buildings overlap another building. Reports counts per section and the worst offenders.
    /// Read-only. Menu: HTownSim > Gap > Audit Map Alignment.
    /// </summary>
    public static class AuditMapAlignment
    {
        private const string CityRoot = "MidtownReal";

        private const string DataFolder = "Assets/HTownSim/Data/";

        /// <summary>A building corner this far (metres) outside its lot counts as spilling into the street.</summary>
        private const float SpillDistance = 1.5f;

        /// <summary>An object shorter than this (metres) is flat ground work such as a parking lot, not a building.</summary>
        private const float FlatHeight = 1.5f;

        /// <summary>The lot data of each part of the map and the scene container that holds its frame (the data's own frame, which the container may turn).</summary>
        private static readonly (string File, string? Container)[] Sources =
        {
            ("MidtownExpanded.txt", null),
            ("Gap_Southwest.txt", null),
            ("Gap_South.txt", null),
            ("Neighborhood_SouthMidtown.txt", "Neighborhood_SouthMidtown"),
            ("Neighborhood_MedCenter.txt", "Neighborhood_MedCenter"),
            ("Neighborhood_Station16.txt", "Neighborhood_Station16"),
        };

        [MenuItem("HTownSim/Gap/Audit Map Alignment")]
        private static void AuditFromMenu()
        {
            Debug.Log(Run());
        }

        /// <summary>Runs the audit and returns the report.</summary>
        public static string Run()
        {
            var root = GameObject.Find(CityRoot);

            if (root == null)
            {
                return "No " + CityRoot + " in the open scene.";
            }

            Physics.SyncTransforms();

            var parcels = LoadWorldParcels(root.transform);

            var index = new GapParcelIndex(parcels);

            var report = new StringBuilder();

            report.AppendLine("Lots loaded in world space: " + parcels.Count);

            report.AppendLine();

            report.AppendLine("OBJECTS standing where there is no lot or sidewalk");

            foreach (var group in ObjectGroups(root.transform))
            {
                report.AppendLine(Objects(group.Name, group.Transform, parcels, index));
            }

            report.AppendLine();

            report.AppendLine("BUILDINGS outside their lot / spilling into the street / overlapping another building");

            foreach (var group in BuildingGroups(root.transform))
            {
                report.AppendLine(Buildings(group.Name, group.Transform, index));
            }

            report.AppendLine();

            report.AppendLine(SeamReport(root.transform, index));

            return report.ToString();
        }

        /// <summary>
        /// Where the gap buildings that spill out of their lot or overlap another building are, relative to the seams where the gap meets another
        /// region (the Midtown window, South Midtown, the Medical Center and the rotated Station 16 rectangle), against all gap buildings.
        /// </summary>
        private static string SeamReport(Transform root, GapParcelIndex index)
        {
            var seams = SeamDistances(root);

            var sb = new StringBuilder();

            sb.AppendLine("GAP BUILDINGS by distance to the nearest seam with another region (all / spilling / overlapping)");

            var bands = new[] { 30f, 100f, 300f, float.MaxValue };

            var all = new int[bands.Length];

            var spilled = new int[bands.Length];

            var overlapped = new int[bands.Length];

            foreach (var container in new[] { "Gap_Placeholders", "Gap_South" })
            {
                var group = root.Find(container)?.Find("GapBuildings");

                if (group == null)
                {
                    continue;
                }

                foreach (Transform building in group)
                {
                    var renderers = building.GetComponentsInChildren<Renderer>();

                    if (renderers.Length == 0)
                    {
                        continue;
                    }

                    var bounds = renderers[0].bounds;

                    var centre = new Vector2(building.position.x, building.position.z);

                    var distance = seams(centre);

                    var band = System.Array.FindIndex(bands, limit => distance <= limit);

                    all[band]++;

                    var parcel = index.LotAt(centre);

                    if (parcel != null && Corners(building, bounds).Any(corner => GapSidewalk.DistanceOutside(parcel.Polygon, corner) > SpillDistance))
                    {
                        spilled[band]++;
                    }

                    if (Overlaps(building, bounds))
                    {
                        overlapped[band]++;
                    }
                }
            }

            for (var i = 0; i < bands.Length; i++)
            {
                var label = i == 0 ? "within 30 m" : i == bands.Length - 1 ? "more than 300 m" : "within " + bands[i].ToString("0") + " m";

                sb.AppendLine(string.Format("  {0,-16} {1,5} buildings, {2,4} spill ({3,4:0.0}%), {4,4} overlap ({5,4:0.0}%)", label, all[i], spilled[i], all[i] == 0 ? 0f : 100f * spilled[i] / all[i], overlapped[i], all[i] == 0 ? 0f : 100f * overlapped[i] / all[i]));
            }

            return sb.ToString();
        }

        /// <summary>A function giving the distance from a world point to the nearest seam: the edge of any other region's area.</summary>
        private static System.Func<Vector2, float> SeamDistances(Transform root)
        {
            var rects = new List<(Rect Rect, Transform? Frame)>
            {
                (Rect.MinMaxRect(-845f, -450f, 560f, 1575f), null),
                (Rect.MinMaxRect(-845f, -1450f, 560f, -450f), null),
                (Rect.MinMaxRect(-450f, -4700f, 650f, -3300f), null),
            };

            var station16 = root.Find("Neighborhood_Station16");

            if (station16 != null)
            {
                rects.Add((Rect.MinMaxRect(-3421f, -1225f, -2121f, 75f), station16));
            }

            return point =>
            {
                var best = float.MaxValue;

                foreach (var (rect, frame) in rects)
                {
                    var local = frame == null ? point : new Vector2(frame.InverseTransformPoint(new Vector3(point.x, 0f, point.y)).x, frame.InverseTransformPoint(new Vector3(point.x, 0f, point.y)).z);

                    var dx = Mathf.Max(rect.xMin - local.x, 0f, local.x - rect.xMax);

                    var dz = Mathf.Max(rect.yMin - local.y, 0f, local.y - rect.yMax);

                    var outside = new Vector2(dx, dz).magnitude;

                    var edge = outside > 0f ? outside : Mathf.Min(local.x - rect.xMin, rect.xMax - local.x, local.y - rect.yMin, rect.yMax - local.y);

                    best = Mathf.Min(best, edge);
                }

                return best;
            };
        }

        /// <summary>Describes the building nearest a world position and the lot found for it (to understand one flagged building).</summary>
        public static string Explain(float x, float z)
        {
            var root = GameObject.Find(CityRoot);

            Physics.SyncTransforms();

            var parcels = LoadWorldParcels(root.transform);

            var index = new GapParcelIndex(parcels);

            Transform? best = null;

            var bestDistance = float.MaxValue;

            foreach (var group in BuildingGroups(root.transform))
            {
                foreach (Transform building in group.Transform)
                {
                    var distance = (new Vector2(building.position.x, building.position.z) - new Vector2(x, z)).magnitude;

                    var renderers = building.GetComponentsInChildren<Renderer>();

                    if (renderers.Length > 0)
                    {
                        var b = renderers[0].bounds;

                        foreach (var renderer in renderers)
                        {
                            b.Encapsulate(renderer.bounds);
                        }

                        distance = (new Vector2(b.center.x, b.center.z) - new Vector2(x, z)).magnitude;
                    }

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;

                        best = building;
                    }
                }
            }

            if (best == null)
            {
                return "no building";
            }

            var rs = best.GetComponentsInChildren<Renderer>();

            var bounds = rs[0].bounds;

            foreach (var renderer in rs)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            var sb = new StringBuilder();

            sb.AppendLine("building " + best.name + " (" + best.parent!.name + ") children=" + best.childCount + " bounds centre " + bounds.center + " size " + bounds.size + " yaw " + best.eulerAngles.y.ToString("0.0"));

            foreach (Transform child in best)
            {
                var box = child.GetComponent<BoxCollider>();

                sb.AppendLine("   child " + child.name + (box == null ? "" : " BoxCollider size " + box.size + " lossy " + child.lossyScale + " world centre " + child.TransformPoint(box.center)));
            }

            var parcel = index.LotAt(new Vector2(bounds.center.x, bounds.center.z));

            if (parcel == null)
            {
                sb.AppendLine("no lot at the building's centre");
            }
            else
            {
                sb.AppendLine("lot index " + parcel.Index + " area " + parcel.Area + " bounds " + string.Join(",", parcel.Bounds.Select(v => v.ToString("0"))));

                foreach (var corner in Corners(best, bounds))
                {
                    sb.AppendLine("   corner (" + corner.x.ToString("0.0") + "," + corner.y.ToString("0.0") + ") outside the lot by " + GapSidewalk.DistanceOutside(parcel.Polygon, corner).ToString("0.0") + " m");
                }
            }

            return sb.ToString();
        }

        /// <summary>Every lot of the map with its outline moved into world space (the container's rotation applied), indexed for lookup.</summary>
        internal static List<GapParcel> LoadWorldParcels(Transform root, System.Func<string, bool>? include = null)
        {
            var all = new List<GapParcel>();

            foreach (var (file, containerName) in Sources)
            {
                if (include != null && !include(file))
                {
                    continue;
                }

                var container = containerName == null ? null : root.Find(containerName);

                foreach (var parcel in GapParcelReader.Read(DataFolder + file))
                {
                    if (container != null)
                    {
                        parcel.Polygon = ToWorld(container, parcel.Polygon);

                        parcel.Bounds = BoundsOf(parcel.Polygon);
                    }

                    all.Add(parcel);
                }
            }

            return all;
        }

        private static float[] ToWorld(Transform container, float[] polygon)
        {
            var result = new float[polygon.Length];

            for (var i = 0; i + 1 < polygon.Length; i += 2)
            {
                var world = container.TransformPoint(new Vector3(polygon[i], 0f, polygon[i + 1]));

                result[i] = world.x;

                result[i + 1] = world.z;
            }

            return result;
        }

        private static float[] BoundsOf(float[] polygon)
        {
            var minX = float.MaxValue;

            var minZ = float.MaxValue;

            var maxX = float.MinValue;

            var maxZ = float.MinValue;

            for (var i = 0; i + 1 < polygon.Length; i += 2)
            {
                minX = Mathf.Min(minX, polygon[i]);

                maxX = Mathf.Max(maxX, polygon[i]);

                minZ = Mathf.Min(minZ, polygon[i + 1]);

                maxZ = Mathf.Max(maxZ, polygon[i + 1]);
            }

            return new[] { minX, minZ, maxX, maxZ };
        }

        /// <summary>The groups of street furniture and parked cars to check.</summary>
        private static IEnumerable<(string Name, Transform Transform)> ObjectGroups(Transform root)
        {
            foreach (var name in new[] { "StreetTrees", "StreetProps", "ParkedCars", "StreetTrees_Exp", "StreetProps_Exp", "ParkedCars_Exp", "InventoryTrees" })
            {
                var group = root.Find(name);

                if (group != null)
                {
                    yield return (name, group);
                }
            }

            foreach (var container in new[] { "Neighborhood_Station16", "Neighborhood_SouthMidtown", "Neighborhood_MedCenter", "Gap_Placeholders", "Gap_South" })
            {
                var parent = root.Find(container);

                if (parent == null)
                {
                    continue;
                }

                foreach (Transform section in parent)
                {
                    if (section.name.StartsWith("Street") || section.name.StartsWith("Parked") || section.name.StartsWith("GapStreet") || section.name.StartsWith("GapParked"))
                    {
                        yield return (container + "/" + section.name, section);
                    }
                }
            }
        }

        /// <summary>The groups of buildings to check.</summary>
        private static IEnumerable<(string Name, Transform Transform)> BuildingGroups(Transform root)
        {
            foreach (var name in new[] { "Buildings", "Buildings_Exp", "MedCenterSkyline", "CorridorBuildings" })
            {
                var group = root.Find(name);

                if (group != null)
                {
                    yield return (name, group);
                }
            }

            foreach (var (container, section) in new[]
            {
                ("Neighborhood_Station16", "Buildings_Station16"),
                ("Neighborhood_SouthMidtown", "Buildings_SouthMidtown"),
                ("Neighborhood_MedCenter", "Buildings_MedCenter"),
                ("Gap_Placeholders", "GapBuildings"),
                ("Gap_South", "GapBuildings"),
            })
            {
                var group = root.Find(container)?.Find(section);

                if (group != null)
                {
                    yield return (container + "/" + section, group);
                }
            }
        }

        /// <summary>Counts the objects of a group that are on no lot or sidewalk, with the worst few positions.</summary>
        private static string Objects(string name, Transform group, List<GapParcel> parcels, GapParcelIndex index)
        {
            var checkedCount = 0;

            var off = new List<Vector3>();

            foreach (Transform item in group)
            {
                if (!item.gameObject.activeInHierarchy)
                {
                    continue;
                }

                checkedCount++;

                if (!OnLand(item.position, index))
                {
                    off.Add(item.position);
                }
            }

            var worst = string.Join(" ", off.Take(3).Select(p => "(" + p.x.ToString("0") + "," + p.z.ToString("0") + ")"));

            return string.Format("  {0,-46} {1,5} checked, {2,5} off the land ({3,4:0.0}%) {4}", name, checkedCount, off.Count, checkedCount == 0 ? 0f : 100f * off.Count / checkedCount, worst);
        }

        private static bool OnLand(Vector3 position, GapParcelIndex index)
        {
            return index.OnLotOrSidewalk(new Vector2(position.x, position.z));
        }

        /// <summary>For each building of a group: is its centre on a lot, do its corners spill beyond the lot, does it overlap another building.</summary>
        private static string Buildings(string name, Transform group, GapParcelIndex index)
        {
            var total = 0;

            var centreOff = 0;

            var spill = 0;

            var overlap = 0;

            var samples = new List<Vector3>();

            foreach (Transform building in group)
            {
                if (!building.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var renderers = building.GetComponentsInChildren<Renderer>();

                if (renderers.Length == 0)
                {
                    continue;
                }

                var bounds = renderers[0].bounds;

                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                }

                // Parking lots and other flat ground objects are not buildings; they are not held to a footprint.
                if (bounds.size.y < FlatHeight)
                {
                    continue;
                }

                total++;

                var centre = new Vector2(bounds.center.x, bounds.center.z);

                var parcel = index.LotAt(centre);

                if (parcel == null)
                {
                    centreOff++;

                    samples.Add(bounds.center);

                    continue;
                }

                var corners = Corners(building, bounds);

                if (corners.Any(corner => GapSidewalk.DistanceOutside(parcel.Polygon, corner) > SpillDistance))
                {
                    spill++;

                    samples.Add(bounds.center);
                }

                if (Overlaps(building, bounds))
                {
                    overlap++;
                }
            }

            var worst = string.Join(" ", samples.Take(3).Select(p => "(" + p.x.ToString("0") + "," + p.z.ToString("0") + ")"));

            return string.Format("  {0,-46} {1,5} checked, {2,4} not on a lot, {3,4} spill, {4,4} overlap another {5}", name, total, centreOff, spill, overlap, worst);
        }

        /// <summary>The four ground corners of a building: its box collider's when it has one (oriented), else its renderers' bounds.</summary>
        private static Vector2[] Corners(Transform building, Bounds bounds)
        {
            var box = building.GetComponentsInChildren<BoxCollider>(true).FirstOrDefault();

            if (box == null)
            {
                return new[]
                {
                    new Vector2(bounds.min.x, bounds.min.z), new Vector2(bounds.max.x, bounds.min.z), new Vector2(bounds.max.x, bounds.max.z), new Vector2(bounds.min.x, bounds.max.z),
                };
            }

            var half = box.size / 2f;

            return new[] { new Vector3(-half.x, 0f, -half.z), new Vector3(half.x, 0f, -half.z), new Vector3(half.x, 0f, half.z), new Vector3(-half.x, 0f, half.z) }
                .Select(corner =>
                {
                    var world = box.transform.TransformPoint(box.center + corner);

                    return new Vector2(world.x, world.z);
                })
                .ToArray();
        }

        /// <summary>True when the building's box collider overlaps the collider of a different building (not the ground).</summary>
        private static bool Overlaps(Transform building, Bounds bounds)
        {
            var box = building.GetComponentsInChildren<BoxCollider>(true).FirstOrDefault();

            if (box == null)
            {
                return false;
            }

            var half = Vector3.Scale(box.size, box.transform.lossyScale) / 2f * 0.9f;

            var centre = box.transform.TransformPoint(box.center) + (Vector3.up * 1.5f);

            half.y = Mathf.Max(0.5f, half.y - 1.5f);

            foreach (var hit in Physics.OverlapBox(centre, half, box.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == building || hit.transform.IsChildOf(building) || building.IsChildOf(hit.transform))
                {
                    continue;
                }

                if (hit is BoxCollider && hit.transform.position.y < 1000f && IsBuildingCollider(hit))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True for a collider that belongs to a building (a body, podium or kit building), not the ground or a street.</summary>
        private static bool IsBuildingCollider(Collider hit)
        {
            var path = hit.transform.name + "/" + (hit.transform.parent == null ? string.Empty : hit.transform.parent.name);

            return path.StartsWith("Body") || path.StartsWith("Podium") || (path.StartsWith("B") && char.IsDigit(path.Length > 1 ? path[1] : ' '));
        }
    }
}
