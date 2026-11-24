#nullable enable
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Puts Station 7's three apparatus in the order Ambulance 07, Ladder 07, Engine 07 as seen from the street (left to right).
/// Seen from the street the camera looks toward -Z, so image right is -X: the left bay is x 20.3, the middle 12.3, the
/// right 4.3. Apply moves Medic07 (now the ambulance model) to 20.3, the driven truck (the ladder, still the object called
/// Engine07) to 12.3 so runs start from the middle bay, and the parked Engine07Parked to 4.3, and relabels the three bay
/// signs AMBULANCE 7, LADDER 7 and ENGINE 7. Revert restores the previous order (ladder, engine, medic) and signs.
/// Refuses to run in Play Mode or with unsaved scene changes; reopens the scene after saving.
/// </summary>
public static class ReorderBays
{
    private const float LeftBay = 20.3f;

    private const float MiddleBay = 12.3f;

    private const float RightBay = 4.3f;

    public static string Apply()
    {
        return Move(ambulance: LeftBay, driven: MiddleBay, engine: RightBay, labels: new[] { "AMBULANCE 7", "LADDER 7", "ENGINE 7" });
    }

    public static string Revert()
    {
        return Move(ambulance: RightBay, driven: LeftBay, engine: MiddleBay, labels: new[] { "MEDIC 7", "ENGINE 7", "LADDER 7" });
    }

    /// <summary>labels are for the left, middle and right bays.</summary>
    private static string Move(float ambulance, float driven, float engine, string[] labels)
    {
        var scene = EditorSceneManager.GetActiveScene();

        if (EditorApplication.isPlaying || scene.isDirty)
        {
            throw new InvalidOperationException("Exit Play Mode and save or discard scene changes first.");
        }

        SetX(GameObject.Find("Medic07").transform, ambulance);

        SetX(GameObject.Find("Engine07").transform, driven);

        SetX(GameObject.Find("Engine07Parked").transform, engine);

        var station = GameObject.Find("Station07");

        var changed = 0;

        foreach (var text in station.GetComponentsInChildren<TMP_Text>(true).Where(t => t.name.StartsWith("Label ")))
        {
            var x = text.transform.position.x;

            var index = Mathf.Abs(x - LeftBay) < 0.5f ? 0 : Mathf.Abs(x - MiddleBay) < 0.5f ? 1 : Mathf.Abs(x - RightBay) < 0.5f ? 2 : -1;

            if (index >= 0)
            {
                text.text = labels[index];

                EditorUtility.SetDirty(text);

                changed++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        AssetDatabase.SaveAssets();

        EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);

        return $"Ambulance at x {ambulance}, driven truck at x {driven}, parked engine at x {engine}; {changed} bay signs relabelled. Scene saved.";
    }

    private static void SetX(Transform t, float x)
    {
        var p = t.position;

        t.position = new Vector3(x, p.y, p.z);
    }
}
