using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Switches the HUD city map to the pre-rendered Blender map (Blender\HTown, renders/htown_map_hud.png,
/// copied to <see cref="TexturePath"/>). Sets the texture's import settings, assigns it to CityMap._staticMap on the
/// engine and saves the scene. Refuses to run if the open scene already has unsaved changes, so it never saves
/// someone else's edits. Run it again after replacing the PNG; Revert clears the field (back to the live camera).
/// </summary>
public static class UseBlenderCityMap
{
    /// <summary>Project path of the Blender map image.</summary>
    private const string TexturePath = "Assets/HTownSim/Textures/Map/CityMapBlender.png";

    /// <summary>Largest texture size kept on import; the source is 1421 x 2048.</summary>
    private const int MaxTextureSize = 2048;

    /// <summary>Assigns the Blender map and returns a summary.</summary>
    public static string Apply()
    {
        return Assign(true);
    }

    /// <summary>Clears the Blender map so the live map camera is used again.</summary>
    public static string Revert()
    {
        return Assign(false);
    }

    /// <summary>Shared body of <see cref="Apply"/> and <see cref="Revert"/>.</summary>
    private static string Assign(bool useBlender)
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        var scene = EditorSceneManager.GetActiveScene();

        if (scene.isDirty)
        {
            return $"Scene {scene.name} has unsaved changes; save or discard them first. Nothing was changed.";
        }

        var map = Object
            .FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)
            .FirstOrDefault(b => b.GetType().FullName == "HTownSim.Runtime.CityMap");

        if (map == null)
        {
            return "No CityMap component in the open scene.";
        }

        Texture2D texture = null;

        if (useBlender)
        {
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);

            if (importer == null)
            {
                return $"Missing {TexturePath}.";
            }

            importer.textureType = TextureImporterType.Default;

            importer.sRGBTexture = true;

            importer.mipmapEnabled = true;

            importer.wrapMode = TextureWrapMode.Clamp;

            importer.filterMode = FilterMode.Trilinear;

            importer.anisoLevel = 4;

            importer.maxTextureSize = MaxTextureSize;

            importer.npotScale = TextureImporterNPOTScale.None;

            importer.textureCompression = TextureImporterCompression.CompressedHQ;

            importer.SaveAndReimport();

            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        var serialized = new SerializedObject(map);

        serialized.FindProperty("_staticMap").objectReferenceValue = texture;

        serialized.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        var size = texture == null ? "none" : $"{texture.width}x{texture.height}";

        return $"CityMap on {map.gameObject.name}: static map = {size}; scene saved.";
    }
}
