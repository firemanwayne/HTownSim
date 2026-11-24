using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Restores hand-authored state that BuildRealMidtown resets: the weather's road renderers.</summary>
public static class RepairAfterExpand
{
    /// <summary>Re-wires the weather component to the rebuilt asphalt renderers.</summary>
    public static string Run()
    {
        var engine = GameObject.Find("Engine07");

        // The engine keeps the pose it has: which bay it starts in depends on the truck the scene is saved with.
        var weather = GameObject.Find("EnvironmentWeather").GetComponents<Component>().First(component => component.GetType().Name == "EnvironmentWeather");

        var roads = UnityEngine.Object.FindObjectsByType<Renderer>().Where(r => r.sharedMaterial != null && (r.sharedMaterial.name == "Asphalt" || r.sharedMaterial.name == "LotAsphalt") && r.sharedMaterial.HasProperty("_Smoothness")).ToArray();

        var data = new SerializedObject(weather);

        var array = data.FindProperty("_roads");

        array.arraySize = roads.Length;

        for (var i = 0; i < roads.Length; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = roads[i];
        }

        data.ApplyModifiedPropertiesWithoutUndo();

        return "Engine left at " + engine.transform.position + "; weather wired to " + roads.Length + " road renderers.";
    }
}
