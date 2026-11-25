using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Cleans up the M city map in the open scene: removes the street-name labels (the station labels stay) and makes the HUD canvas scale
/// with screen height, so the incident panel and the whole map stack always fit on screen however wide the window is.
/// Run: unity command script run --file AgentScripts/FitCityMap.cs --entry FitCityMap.Run
/// </summary>
public static class FitCityMap
{
    private const string PanelName = "CityMapPanel";

    private const string MapName = "Map";

    private const string LabelPrefix = "Label ";

    private const string StationLabelPrefix = "Label STN";

    /// <summary>Deletes the street labels, sets the canvas to match screen height, and saves the scene.</summary>
    public static string Run()
    {
        var panel = FindPanel();

        if (panel == null)
        {
            return "No " + PanelName + " in the open scene (stop Play Mode first).";
        }

        var map = panel.transform.Find(MapName);

        if (map == null)
        {
            return PanelName + " has no " + MapName + " child.";
        }

        var doomed = new List<GameObject>();

        foreach (Transform child in map)
        {
            if (child.name.StartsWith(LabelPrefix) && !child.name.StartsWith(StationLabelPrefix))
            {
                doomed.Add(child.gameObject);
            }
        }

        foreach (var label in doomed)
        {
            Undo.DestroyObjectImmediate(label);
        }

        var scaler = panel.GetComponentInParent<CanvasScaler>();

        if (scaler != null)
        {
            scaler.matchWidthOrHeight = 1f;
        }

        // The top bar already says "H T O W N / <unit>", so the map header is just "CITY MAP" and no longer runs under the hide label.
        foreach (var text in panel.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            if (text.transform.parent == panel.transform && text.text.Contains("CITY MAP"))
            {
                text.text = "CITY MAP";
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        EditorSceneManager.SaveOpenScenes();

        return "Removed " + doomed.Count + " street labels; canvas now matches screen height (" + (scaler != null) + ").";
    }

    /// <summary>The map panel, found even when inactive.</summary>
    private static GameObject FindPanel()
    {
        foreach (var rect in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (rect.name == PanelName && rect.gameObject.scene.IsValid())
            {
                return rect.gameObject;
            }
        }

        return null;
    }
}
