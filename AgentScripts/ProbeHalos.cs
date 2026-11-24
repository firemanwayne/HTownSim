using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Play Mode probe: how many lens halos the driven truck has, how many are lit now, and a sample position.</summary>
public static class ProbeHalos
{
    public static string Run()
    {
        var truck = GameObject.Find("Engine07").transform;

        var halos = truck.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("LensHalo_")).ToList();

        var response = truck.GetComponent("FireResponse");

        response.GetType().GetField("_lightsEnabled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(response, true);

        var sb = new StringBuilder();

        sb.AppendLine("halos: " + halos.Count + ", enabled: " + halos.Count(h => h.GetComponent<Renderer>().enabled));

        var flasher = truck.GetComponent("LensFlasher");

        sb.AppendLine("flasher: " + (flasher != null) + " lenses " + (flasher != null ? flasher.GetType().GetProperty("LensMaterialCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(flasher) : "-"));

        var first = halos.FirstOrDefault();

        if (first != null)
        {
            sb.AppendLine("parent lossy " + first.parent.lossyScale + " local " + first.parent.localPosition + " quad lossy " + first.lossyScale);

            sb.AppendLine(first.name + " world " + first.position + " scale " + first.localScale + " parent " + first.parent.name);
        }

        return sb.ToString();
    }
}
