#nullable enable
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HTownSim.Editor
{
    /// <summary>
    /// Places the emergency-room bays of the Texas Medical Center hospitals. An ambulance that has a patient aboard drives to the nearest
    /// <c>ER_Bay_*</c> object (see <c>FireResponse.BeginTransport</c>). Menu: HTownSim > Med Center > Add Hospital ER Bays. Can be run again.
    /// </summary>
    internal static class AddMedCenterBays
    {
        /// <summary>The name of the container that holds the bays.</summary>
        private const string ContainerName = "MedCenterHospitals";

        /// <summary>Latitude of the scene origin (Station 7), as in GeoProjection.</summary>
        private const double OriginLatitude = 29.73948387;

        /// <summary>Longitude of the scene origin (Station 7).</summary>
        private const double OriginLongitude = -95.3734589;

        /// <summary>Street-grid rotation in degrees.</summary>
        private const double GridDegrees = 35.06;

        /// <summary>How far, in metres, the search for open ground around a hospital reaches.</summary>
        private const float MaxSearchRadius = 90f;

        /// <summary>A hospital and its approximate emergency entrance.</summary>
        private readonly struct Hospital
        {
            public Hospital(string name, double latitude, double longitude)
            {
                Name = name;

                Latitude = latitude;

                Longitude = longitude;
            }

            public string Name { get; }

            public double Latitude { get; }

            public double Longitude { get; }
        }

        private static readonly Hospital[] Hospitals =
        {
            new Hospital("Memorial_Hermann", 29.713415, -95.395730),
            new Hospital("Houston_Methodist", 29.709847, -95.399638),
            new Hospital("Texas_Childrens", 29.707839, -95.401617),
            new Hospital("St_Lukes", 29.707970, -95.400405),
            new Hospital("Ben_Taub", 29.710984, -95.394350),
            new Hospital("MD_Anderson", 29.707556, -95.397308),
        };

        [MenuItem("HTownSim/Med Center/Add Hospital ER Bays")]
        public static void Run()
        {
            Apply();
        }

        /// <summary>Places the bays and saves the scene; returns a summary line.</summary>
        public static string Apply()
        {
            if (EditorApplication.isPlaying)
            {
                return "Exit Play Mode first.";
            }

            var city = GameObject.Find("MidtownReal");

            if (city == null)
            {
                return "Build the city first.";
            }

            var old = city.transform.Find(ContainerName);

            if (old != null)
            {
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            }

            var container = new GameObject(ContainerName).transform;

            container.SetParent(city.transform, false);

            Physics.SyncTransforms();

            var blue = MakeMaterial("ErBlue", new Color(0.1f, 0.35f, 0.9f));

            var white = MakeMaterial("ErWhite", new Color(0.95f, 0.95f, 0.95f));

            var red = MakeMaterial("ErRed", new Color(0.85f, 0.1f, 0.1f));

            var report = string.Empty;

            foreach (var hospital in Hospitals)
            {
                var world = ToWorld(hospital.Latitude, hospital.Longitude);

                var spot = FindOpenGround(world, out var moved);

                BuildBay(container, hospital.Name, spot, blue, white, red);

                report += hospital.Name + " at (" + spot.x.ToString("0") + ", " + spot.z.ToString("0") + ") moved " + moved.ToString("0") + " m; ";
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

            return report;
        }

        /// <summary>The scene position (x, 0, z) of a latitude and longitude.</summary>
        private static Vector3 ToWorld(double latitude, double longitude)
        {
            var east = (longitude - OriginLongitude) * 111320d * Math.Cos(OriginLatitude * Math.PI / 180d);

            var north = (latitude - OriginLatitude) * 110574d;

            var rotation = GridDegrees * Math.PI / 180d;

            var x = (east * Math.Cos(rotation)) - (north * Math.Sin(rotation));

            var z = (east * Math.Sin(rotation)) + (north * Math.Cos(rotation));

            return new Vector3((float)x, 0f, (float)z);
        }

        /// <summary>The nearest spot to <paramref name="target"/> with no collider within a truck's footprint (an ambulance-sized clearing).</summary>
        private static Vector3 FindOpenGround(Vector3 target, out float moved)
        {
            moved = 0f;

            for (var radius = 0f; radius <= MaxSearchRadius; radius += 3f)
            {
                var steps = radius < 0.1f ? 1 : Mathf.CeilToInt(radius * 2f * Mathf.PI / 3f);

                for (var i = 0; i < steps; i++)
                {
                    var angle = i * Mathf.PI * 2f / steps;

                    var candidate = target + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                    if (IsOpen(candidate))
                    {
                        moved = radius;

                        return candidate;
                    }
                }
            }

            return target;
        }

        /// <summary>True when an ambulance-sized box standing here touches no solid object above the ground.</summary>
        private static bool IsOpen(Vector3 position)
        {
            var centre = position + new Vector3(0f, 1.8f, 0f);

            var hits = Physics.OverlapBox(centre, new Vector3(3.2f, 1.2f, 3.2f));

            foreach (var hit in hits)
            {
                if (hit.isTrigger)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>A blue pad on the ground, a white cross, and a tall sign so the bay reads from a distance.</summary>
        private static void BuildBay(Transform parent, string name, Vector3 position, Material blue, Material white, Material red)
        {
            var bay = new GameObject("ER_Bay_" + name).transform;

            bay.SetParent(parent, false);

            bay.position = position;

            Piece(bay, "Pad", PrimitiveType.Cube, new Vector3(0f, 0.09f, 0f), new Vector3(7f, 0.08f, 7f), blue);

            Piece(bay, "CrossA", PrimitiveType.Cube, new Vector3(0f, 0.15f, 0f), new Vector3(4.4f, 0.04f, 1.2f), white);

            Piece(bay, "CrossB", PrimitiveType.Cube, new Vector3(0f, 0.15f, 0f), new Vector3(1.2f, 0.04f, 4.4f), white);

            Piece(bay, "Post", PrimitiveType.Cube, new Vector3(3.6f, 3.5f, 3.6f), new Vector3(0.3f, 7f, 0.3f), white);

            Piece(bay, "Sign", PrimitiveType.Cube, new Vector3(3.6f, 7.4f, 3.6f), new Vector3(4.2f, 1.6f, 0.3f), red);

            var label = new GameObject("Label");

            label.transform.SetParent(bay, false);

            label.transform.localPosition = new Vector3(3.6f, 7.4f, 3.4f);

            var text = label.AddComponent<TextMesh>();

            text.text = "EMERGENCY";

            text.anchor = TextAnchor.MiddleCenter;

            text.characterSize = 0.28f;

            text.fontSize = 48;

            text.color = Color.white;
        }

        /// <summary>One primitive child of the bay, with its collider removed so it never blocks the truck or traffic.</summary>
        private static void Piece(Transform parent, string name, PrimitiveType shape, Vector3 local, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(shape);

            go.name = name;

            go.transform.SetParent(parent, false);

            go.transform.localPosition = local;

            go.transform.localScale = scale;

            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());

            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>A URP/Unlit-style flat material saved under Assets/HTownSim/Generated.</summary>
        private static Material MakeMaterial(string name, Color color)
        {
            var path = "Assets/HTownSim/Generated/" + name + ".mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
            {
                return existing;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

            var material = new Material(shader);

            material.color = color;

            AssetDatabase.CreateAsset(material, path);

            return material;
        }
    }
}
