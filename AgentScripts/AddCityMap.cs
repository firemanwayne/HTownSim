using System.IO;
using System.Collections.Generic;
using System;
using System.Linq;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-time authoring pass that adds the third-person city map to the HUD: a top-down map camera, a panel on
/// the left of the screen, live markers, and labels for the Midtown streets. Wires everything into the
/// <c>CityMap</c> runtime component on the engine.
/// </summary>
public static class AddCityMap
{
    private const string PanelName = "CityMapPanel";

    private const float WorldMinX = -520f;

    private const float WorldMinZ = -450f;

    private const float WorldWidth = 1080f;

    private const float WorldDepth = 1070f;

    private const float MapWidthPixels = 360f;

    private const float PanelPadding = 12f;

    private const float TitleHeight = 28f;

    private const float PanelLeft = 28f;

    private const float PanelTop = 345f;

    private const float CameraHeight = 250f;

    private const int UiLayer = 5;

    private static readonly Vector3 StationPosition = new Vector3(12.3f, 0f, -26.6f);

    /// <summary>Adds the map and returns a summary.</summary>
    public static string Build()
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        var canvas = GameObject.Find("ResponseHud").GetComponent<Canvas>();

        var truck = (GameObject.Find("Engine07") ?? GameObject.Find("Engine08"));

        RemoveExistingMap(truck);

        var incident = GameObject.Find("FireIncident").transform;

        var thirdPerson = GameObject.Find("VehicleCamera").GetComponent<CinemachineCamera>();

        var responseType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("HTownSim.Runtime.FireResponse")).First(t => t != null);

        var mapType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("HTownSim.Runtime.CityMap")).First(t => t != null);

        var mapCamera = CreateMapCamera();

        var mapHeight = Mathf.Round(MapWidthPixels * WorldDepth / WorldWidth);

        var panel = CreatePanel(canvas.transform, new Vector2(MapWidthPixels + PanelPadding * 2f, mapHeight + TitleHeight + PanelPadding * 2f));

        CreateLabel(panel.transform, ("H T O W N   /   CITY MAP", new Vector2(PanelPadding + 2f, -6f), new Vector2(300f, 24f), 18, "FFAF52"));

        CreateLabel(panel.transform, ("M  HIDE", new Vector2(MapWidthPixels - 70f, -8f), new Vector2(90f, 22f), 14, "E6D7B7"));

        var mapRect = CreateMapRect(panel.transform, mapHeight);

        var image = mapRect.GetComponent<RawImage>();

        AddStreetLabels(mapRect, mapHeight);

        var stationMarker = CreateMarker(mapRect, ("StationMarker", new Vector2(12f, 12f), "E6D7B7"));

        AddMapLabel(mapRect, ("STN 7", ToMap(StationPosition, mapHeight) + new Vector2(24f, 0f), 0f, 12));

        var incidentMarker = CreateMarker(mapRect, ("IncidentMarker", new Vector2(16f, 16f), "FF5A3C"));

        var playerMarker = CreateMarker(mapRect, ("PlayerMarker", new Vector2(22f, 22f), "7FD4FF"));

        var map = truck.AddComponent(mapType);

        var serialized = new SerializedObject(map);

        serialized.FindProperty("_panel").objectReferenceValue = panel;

        serialized.FindProperty("_mapCamera").objectReferenceValue = mapCamera;

        serialized.FindProperty("_mapImage").objectReferenceValue = image;

        serialized.FindProperty("_mapRect").objectReferenceValue = mapRect;

        serialized.FindProperty("_playerMarker").objectReferenceValue = playerMarker;

        serialized.FindProperty("_incidentMarker").objectReferenceValue = incidentMarker;

        serialized.FindProperty("_stationMarker").objectReferenceValue = stationMarker;

        serialized.FindProperty("_player").objectReferenceValue = truck.transform;

        serialized.FindProperty("_incident").objectReferenceValue = incident;

        serialized.FindProperty("_stationPosition").vector3Value = StationPosition;

        serialized.FindProperty("_response").objectReferenceValue = truck.GetComponent(responseType);

        serialized.FindProperty("_thirdPersonCamera").objectReferenceValue = thirdPerson;

        serialized.FindProperty("_worldMin").vector2Value = new Vector2(WorldMinX, WorldMinZ);

        serialized.FindProperty("_worldSize").vector2Value = new Vector2(WorldWidth, WorldDepth);

        serialized.ApplyModifiedPropertiesWithoutUndo();

        AddControlsHint(canvas.transform);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        return "City map added: panel " + panel.GetComponent<RectTransform>().sizeDelta + ", map " + MapWidthPixels + "x" + mapHeight;
    }

    /// <summary>Removes a previously generated map (panel, camera and component) so it can be rebuilt at a new size.</summary>
    private static void RemoveExistingMap(GameObject truck)
    {
        foreach (var name in new[] { PanelName, "CityMapCamera" })
        {
            var existing = GameObject.Find(name);

            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing);
            }
        }

        foreach (var component in truck.GetComponents<MonoBehaviour>())
        {
            if (component != null && component.GetType().Name == "CityMap")
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }
    }

    /// <summary>Creates the disabled top-down orthographic camera that covers the whole city.</summary>
    private static Camera CreateMapCamera()
    {
        var go = new GameObject("CityMapCamera");

        go.transform.SetPositionAndRotation(new Vector3(WorldMinX + WorldWidth / 2f, CameraHeight, WorldMinZ + WorldDepth / 2f), Quaternion.Euler(90f, 0f, 0f));

        var camera = go.AddComponent<Camera>();

        camera.orthographic = true;

        camera.orthographicSize = WorldDepth / 2f;

        camera.nearClipPlane = 0.3f;

        camera.farClipPlane = 400f;

        camera.clearFlags = CameraClearFlags.SolidColor;

        camera.backgroundColor = Hex("1B2A30");

        camera.cullingMask = ~(1 << UiLayer);

        camera.depth = -10f;

        camera.allowHDR = false;

        camera.allowMSAA = false;

        camera.enabled = false;

        var data = go.AddComponent<UniversalAdditionalCameraData>();

        data.renderShadows = false;

        data.renderPostProcessing = false;

        data.antialiasing = AntialiasingMode.None;

        return camera;
    }

    /// <summary>Creates the dark HUD panel on the left, below the dispatch panel.</summary>
    private static GameObject CreatePanel(Transform parent, Vector2 size)
    {
        var panel = new GameObject(PanelName, typeof(RectTransform), typeof(Image));

        panel.transform.SetParent(parent, false);

        var rect = (RectTransform)panel.transform;

        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);

        rect.anchoredPosition = new Vector2(PanelLeft, -PanelTop);

        rect.sizeDelta = size;

        var image = panel.GetComponent<Image>();

        image.color = Hex("18282F");

        image.raycastTarget = false;

        return panel;
    }

    /// <summary>Creates the map image rect that holds the texture, labels and markers.</summary>
    private static RectTransform CreateMapRect(Transform parent, float height)
    {
        var map = new GameObject("Map", typeof(RectTransform), typeof(RawImage));

        map.transform.SetParent(parent, false);

        var rect = (RectTransform)map.transform;

        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);

        rect.anchoredPosition = new Vector2(PanelPadding, -(TitleHeight + PanelPadding / 2f));

        rect.sizeDelta = new Vector2(MapWidthPixels, height);

        var image = map.GetComponent<RawImage>();

        image.color = Hex("1B2A30");

        image.raycastTarget = false;

        return rect;
    }

    /// <summary>Creates a marker image anchored at the map's lower-left corner, ready to be positioned in code.</summary>
    private static Image CreateMarker(RectTransform parent, (string Name, Vector2 Size, string Hex) spec)
    {
        var go = new GameObject(spec.Name, typeof(RectTransform), typeof(Image));

        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;

        rect.anchorMin = rect.anchorMax = Vector2.zero;

        rect.pivot = new Vector2(0.5f, 0.5f);

        rect.sizeDelta = spec.Size;

        var image = go.GetComponent<Image>();

        image.color = Hex(spec.Hex);

        image.raycastTarget = false;

        return image;
    }

    /// <summary>Adds the Midtown street-name labels over the map.</summary>
    private static void AddStreetLabels(RectTransform mapRect, float mapHeight)
    {
        var labels = new List<(string, Vector3, float)>();

        var northSouth = 0;

        var eastWest = 0;

        foreach (var line in File.ReadAllLines("Assets/HTownSim/Data/MidtownReal.txt"))
        {
            var f = line.Split('\t');

            if (f[0] != "G")
            {
                continue;
            }

            var centre = float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture);

            if (f[1] == "ns" && centre > WorldMinX + 10f && centre < WorldMinX + WorldWidth - 10f)
            {
                labels.Add((f[4], new Vector3(centre, 0f, WorldMinZ + 90f + (northSouth++ % 3) * 200f), 90f));
            }
            else if (f[1] == "ew" && centre > WorldMinZ + 10f && centre < WorldMinZ + WorldDepth - 10f)
            {
                labels.Add((f[4], new Vector3(WorldMinX + 110f + (eastWest++ % 3) * 220f, 0f, centre), 0f));
            }
        }
        foreach (var label in labels)
        {
            AddMapLabel(mapRect, (label.Item1, ToMap(label.Item2, mapHeight), label.Item3, 12));
        }
    }

    /// <summary>Adds one centred map label with a translucent backing so it reads over streets and buildings.</summary>
    private static void AddMapLabel(RectTransform mapRect, (string Text, Vector2 Position, float Rotation, int Size) spec)
    {
        var go = new GameObject("Label " + spec.Text, typeof(RectTransform), typeof(Image));

        go.transform.SetParent(mapRect, false);

        var rect = (RectTransform)go.transform;

        rect.anchorMin = rect.anchorMax = Vector2.zero;

        rect.pivot = new Vector2(0.5f, 0.5f);

        rect.anchoredPosition = spec.Position;

        rect.sizeDelta = new Vector2(spec.Text.Length * spec.Size * 0.62f + 8f, spec.Size + 6f);

        rect.localRotation = Quaternion.Euler(0f, 0f, spec.Rotation);

        var backing = go.GetComponent<Image>();

        backing.color = new Color(0.094f, 0.157f, 0.184f, 0.78f);

        backing.raycastTarget = false;

        var text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();

        text.transform.SetParent(go.transform, false);

        text.rectTransform.anchorMin = Vector2.zero;

        text.rectTransform.anchorMax = Vector2.one;

        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;

        text.text = spec.Text;

        text.fontSize = spec.Size;

        text.fontStyle = FontStyles.Bold;

        text.alignment = TextAlignmentOptions.Center;

        text.textWrappingMode = TextWrappingModes.NoWrap;

        text.color = Hex("E6D7B7");

        text.raycastTarget = false;
    }

    /// <summary>Adds a HUD title or hint label, anchored top-left inside <paramref name="parent"/>.</summary>
    private static void CreateLabel(Transform parent, (string Text, Vector2 Position, Vector2 Size, int FontSize, string Hex) spec)
    {
        var text = new GameObject("HudLabel", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();

        text.transform.SetParent(parent, false);

        text.rectTransform.anchorMin = text.rectTransform.anchorMax = text.rectTransform.pivot = new Vector2(0f, 1f);

        text.rectTransform.anchoredPosition = spec.Position;

        text.rectTransform.sizeDelta = spec.Size;

        text.text = spec.Text;

        text.fontSize = spec.FontSize;

        text.fontStyle = FontStyles.Bold;

        text.characterSpacing = 1f;

        text.color = Hex(spec.Hex);

        text.raycastTarget = false;
    }

    /// <summary>Appends the map key to the bottom-left controls hint.</summary>
    private static void AddControlsHint(Transform canvas)
    {
        foreach (var text in canvas.GetComponentsInChildren<TMP_Text>())
        {
            if (text.text.StartsWith("WASD", StringComparison.Ordinal) && !text.text.Contains("MAP"))
            {
                text.text += "     M  MAP";
            }
        }
    }

    /// <summary>Converts a world position to a position in the map rect (lower-left origin).</summary>
    private static Vector2 ToMap(Vector3 world, float mapHeight)
    {
        return new Vector2((world.x - WorldMinX) / WorldWidth * MapWidthPixels, (world.z - WorldMinZ) / WorldDepth * mapHeight);
    }

    /// <summary>Parses a hex colour.</summary>
    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var colour);

        return colour;
    }
}
