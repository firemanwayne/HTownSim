#nullable enable
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Play Mode look at the driven Ambulance 07 with its emergency lights and headlights on: front and rear three-quarter views
/// and a close side view, written to Blender\HFD Apparatus\EMS\unity_amb_*.png. Start via CaptureAmbulance.Start.
/// </summary>
public static class CaptureAmbulance
{
    public static readonly string Folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "Blender", "HFD Apparatus", "EMS"));

    public const string LogPath = "J:/Unity/projects/HTownSim/Temp/capture_amb.log";

    public static string Start()
    {
        File.Delete(LogPath);

        new GameObject("AmbulanceCaptureDriver").AddComponent<AmbulanceCaptureDriver>();

        return "Started.";
    }

    public static string Report()
    {
        return File.Exists(LogPath) ? File.ReadAllText(LogPath) : "No log yet.";
    }
}

public sealed class AmbulanceCaptureDriver : MonoBehaviour
{
    private IEnumerator Start()
    {
        var truck = GameObject.Find("Engine07").transform;

        var controller = truck.GetComponent<CharacterController>();

        controller.enabled = false;

        truck.SetPositionAndRotation(new Vector3(-622f, 0.2f, 250f), Quaternion.identity);

        controller.enabled = true;

        yield return new WaitForSecondsRealtime(1.5f);

        var cam = new GameObject("AmbCam") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();

        if (Camera.main != null)
        {
            cam.CopyFrom(Camera.main);
        }

        cam.fieldOfView = 40f;

        var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 4 };

        cam.targetTexture = rt;

        void Shot(string name, Vector3 local, Vector3 lookLocal)
        {
            cam.transform.position = truck.TransformPoint(local);

            cam.transform.LookAt(truck.TransformPoint(lookLocal));

            cam.Render();

            RenderTexture.active = rt;

            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);

            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);

            tex.Apply();

            File.WriteAllBytes(Path.Combine(CaptureAmbulance.Folder, "unity_amb_" + name + ".png"), tex.EncodeToPNG());

            RenderTexture.active = null;

            Destroy(tex);
        }

        // The glows face Camera.main, so make the capture camera the main one while shooting.
        var gameCamera = Camera.main;

        var gameTag = gameCamera != null ? gameCamera.tag : "MainCamera";

        if (gameCamera != null)
        {
            gameCamera.tag = "Untagged";
        }

        cam.tag = "MainCamera";

        var response = truck.GetComponent("FireResponse");

        response.GetType().GetField("_lightsEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(response, true);

        // Wait for a moment when group A (red) is lit, then for group B (clear).
        yield return new WaitForSecondsRealtime(0.15f);

        Shot("front34_a", new Vector3(-6f, 2.6f, 8f), new Vector3(0f, 1.3f, 0.5f));

        Shot("rear34_a", new Vector3(5f, 2.6f, -8f), new Vector3(0f, 1.3f, -1f));

        yield return new WaitForSecondsRealtime(0.31f);

        Shot("front34_b", new Vector3(-6f, 2.6f, 8f), new Vector3(0f, 1.3f, 0.5f));

        Shot("close_front", new Vector3(-3.2f, 1.5f, 6.2f), new Vector3(0f, 1.2f, 3f));

        Shot("side", new Vector3(8f, 1.8f, 0.2f), new Vector3(0f, 1.3f, 0.2f));

        response.GetType().GetField("_lightsEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(response, false);

        if (gameCamera != null)
        {
            gameCamera.tag = gameTag;
        }

        cam.targetTexture = null;

        Destroy(rt);

        Destroy(cam.gameObject);

        File.WriteAllText(CaptureAmbulance.LogPath, "DONE");

        Destroy(gameObject);
    }
}
