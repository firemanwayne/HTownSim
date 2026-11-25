using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the HPD patrol unit prefab the game parks at police incidents (LiveIncidentPolice loads "HpdPatrol" from Resources): the
/// exported model (Assets/HTownSim/Vehicles/HpdPatrol/HpdPatrol.fbx, from Blender/HPD/export_hpd.py) with a box collider. Can be run again.
/// Run: unity command script run --file AgentScripts/BuildHpdPatrolPrefab.cs --entry BuildHpdPatrolPrefab.Run
/// </summary>
public static class BuildHpdPatrolPrefab
{
    private const string ModelPath = "Assets/HTownSim/Vehicles/HpdPatrol/HpdPatrol.fbx";

    private const string PrefabFolder = "Assets/HTownSim/Resources";

    private const string PrefabPath = PrefabFolder + "/HpdPatrol.prefab";

    /// <summary>Creates or replaces the prefab.</summary>
    public static string Run()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        if (model == null)
        {
            return "Missing " + ModelPath + ". Run Blender/HPD/export_hpd.py first.";
        }

        Directory.CreateDirectory(PrefabFolder);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);

        instance.name = "HpdPatrol";

        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // The model's body is 2.6 m wide, 2.06 m tall and 5.1 m long, with its origin on the ground at the centre of the wheelbase.
        var collider = instance.AddComponent<BoxCollider>();

        collider.center = new Vector3(0f, 1.0f, 0.06f);

        collider.size = new Vector3(2.4f, 1.9f, 5.1f);

        foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);

        Object.DestroyImmediate(instance);

        AssetDatabase.SaveAssets();

        return "Saved " + PrefabPath;
    }
}
