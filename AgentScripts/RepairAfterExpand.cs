using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Restores hand-authored state that BuildRealMidtown resets: Engine 7's bay pose and the weather's road renderers.</summary>
public static class RepairAfterExpand
{
    /// <summary>Puts the engine back in its bay and re-wires the weather component to the rebuilt asphalt renderers.</summary>
    public static string Run()
    {
        var engine = GameObject.Find("Engine07");

        var controller = engine.GetComponent<CharacterController>();

        controller.enabled = false;

        engine.transform.SetPositionAndRotation(new Vector3(12.3f, 0.2f, -20.8f), Quaternion.identity);

        controller.enabled = true;

        var weather = GameObject.Find("EnvironmentWeather").GetComponent("EnvironmentWeather");

        var roads = UnityEngine.Object.FindObjectsByType<Renderer>().Where(r => r.sharedMaterial != null && (r.sharedMaterial.name == "Asphalt" || r.sharedMaterial.name == "LotAsphalt") && r.sharedMaterial.HasProperty("_Smoothness")).ToArray();

        var data = new SerializedObject(weather);

        var array = data.FindProperty("_roads");

        array.arraySize = roads.Length;

        for (var i = 0; i < roads.Length; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = roads[i];
        }

        data.ApplyModifiedPropertiesWithoutUndo();

        return "Engine restored at " + engine.transform.position + "; weather wired to " + roads.Length + " road renderers; weather root active=" + GameObject.Find("EnvironmentWeather").activeInHierarchy + ", Ladder07 active=" + GameObject.Find("Ladder07").activeInHierarchy + ", Medic07 active=" + GameObject.Find("Medic07").activeInHierarchy;
    }
}
