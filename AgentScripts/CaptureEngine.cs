#nullable enable
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Play Mode look at Engine 07: moves it to an open street (Milam St), renders four views (front three-quarter, right
/// side, rear, and front three-quarter with the emergency lights on) to Blender\HFD Apparatus\Engines\unity_*.png,
/// then drives forward a little and reports how far the wheels rolled. Start via CaptureEngine.Start; Report ~6 s later.
/// </summary>
public static class CaptureEngine
{
    public static readonly string Folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "Blender", "HFD Apparatus", "Engines"));

    public const string LogPath = "J:/Unity/projects/HTownSim/Temp/capture_engine.log";

    public static string Start()
    {
        File.Delete(LogPath);

        new GameObject("EngineCaptureDriver").AddComponent<EngineCaptureDriver>();

        return "Started.";
    }

    public static string Report()
    {
        return File.Exists(LogPath) ? File.ReadAllText(LogPath) : "No log yet.";
    }
}

public sealed class EngineCaptureDriver : MonoBehaviour
{
    private IEnumerator Start()
    {
        var truck = GameObject.Find("Engine07").transform;

        var controller = truck.GetComponent<CharacterController>();

        controller.enabled = false;

        truck.SetPositionAndRotation(new Vector3(-622f, 0.2f, 250f), Quaternion.identity);

        controller.enabled = true;

        yield return new WaitForSecondsRealtime(1.5f);

        var cam = new GameObject("EngineCam") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();

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

            File.WriteAllBytes(Path.Combine(CaptureEngine.Folder, "unity_" + name + ".png"), tex.EncodeToPNG());

            RenderTexture.active = null;

            Destroy(tex);
        }

        Shot("front34", new Vector3(-9f, 3.2f, 11f), new Vector3(0f, 1.4f, 0.5f));

        Shot("right", new Vector3(14f, 2.0f, 0f), new Vector3(0f, 1.4f, 0f));

        Shot("rear", new Vector3(-3f, 2.6f, -13f), new Vector3(0f, 1.6f, -2f));

        var response = truck.GetComponent("FireResponse");

        response.GetType().GetField("_lightsEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(response, true);

        yield return new WaitForSecondsRealtime(0.4f);

        Shot("front34_lights", new Vector3(-9f, 3.2f, 11f), new Vector3(0f, 1.4f, 0.5f));

        response.GetType().GetField("_lightsEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(response, false);

        cam.targetTexture = null;

        Destroy(rt);

        Destroy(cam.gameObject);

        // Roll check: drive the truck 10 m forward by moving it and watch WheelSpin's roll angle.
        var spin = truck.GetComponent("WheelSpin");

        var wheels = (int)spin.GetType().GetProperty("WheelCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(spin);

        var rollProperty = spin.GetType().GetProperty("Roll", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var rolled = 0f;

        var last = (float)rollProperty.GetValue(spin);

        for (var i = 0; i < 20; i++)
        {
            controller.Move(truck.forward * 0.25f);

            yield return null;

            var now = (float)rollProperty.GetValue(spin);

            rolled += Mathf.DeltaAngle(last, now);

            last = now;
        }

        var hi3dWheel = truck.Find("Engine07CleanVisual/WheelFrontLeft") ?? truck.Find("Engine07Hi3DVisual/WheelFrontLeft");

        File.WriteAllText(CaptureEngine.LogPath,
            $"wheels found by WheelSpin: {wheels}\nrolled {rolled:F0} deg over 5 m (expected {5f / 0.54f * Mathf.Rad2Deg:F0})\n" +
            $"Hi3D front-left wheel rotation: {(hi3dWheel != null ? hi3dWheel.localEulerAngles.ToString() : "missing")}\nDONE");

        Destroy(gameObject);
    }
}
