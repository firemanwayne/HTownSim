using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Assigns the imported station ring recording to FireResponse and saves the scene.</summary>
public static class WireDispatchRing
{
    private const string ClipPath = "Assets/HTownSim/Audio/hfd_mdt_ring.mp3";

    /// <summary>Wires the clip into the saved HoustonResponse scene.</summary>
    public static string Run()
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);

        if (clip == null)
        {
            return "Clip not imported yet: " + ClipPath;
        }

        var data = new SerializedObject(GameObject.Find("Engine07").GetComponent("FireResponse"));

        data.FindProperty("_dispatchRing").objectReferenceValue = clip;

        data.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        return "Wired " + clip.name + " (" + clip.length.ToString("F2") + " s) and saved the scene.";
    }
}
