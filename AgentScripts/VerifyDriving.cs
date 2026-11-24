using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Play Mode check of the driving changes: holds S (reverse), W (accelerate) and Space (brake) through the Input System and logs the
/// top speed reached, the rear lamp and alarm state, and the brake sounds. Start via VerifyDriving.Start, read via VerifyDriving.Report.
/// </summary>
public static class VerifyDriving
{
    /// <summary>Where the driver writes its findings.</summary>
    public const string LogPath = "J:/Unity/projects/HTownSim/Temp/verify_driving.log";

    /// <summary>Folder for the rear-view captures.</summary>
    public const string ShotFolder = "J:/Unity/projects/HTownSim/Temp/";

    /// <summary>Starts the scripted drive.</summary>
    public static string Start()
    {
        File.Delete(LogPath);

        new GameObject("DrivingTestDriver").AddComponent<DrivingTestDriver>();

        return "Started.";
    }

    /// <summary>Returns the log.</summary>
    public static string Report()
    {
        return File.Exists(LogPath) ? File.ReadAllText(LogPath) : "No log yet.";
    }
}

/// <summary>Holds keys and records what the truck, lamps and sound sources do.</summary>
public sealed class DrivingTestDriver : MonoBehaviour
{
    private readonly StringBuilder _log = new StringBuilder();

    private Component _response;

    private Transform _truck;

    private IEnumerator Start()
    {
        _truck = GameObject.Find("Engine07").transform;

        _response = _truck.GetComponent("FireResponse");

        yield return new WaitForSeconds(2f);

        var controller = _truck.GetComponent<CharacterController>();

        controller.enabled = false;

        _truck.position = new Vector3(-622f, 0.3f, 200f);

        _truck.rotation = Quaternion.identity;

        controller.enabled = true;

        yield return new WaitForSeconds(2f);

        Say("start speed " + Get<float>("SignedSpeed"));

        Hold(Key.S);

        yield return new WaitForSeconds(2.5f);

        Say("reverse: speed " + Get<float>("SignedSpeed").ToString("F1") + " reverseEngaged " + Get<bool>("ReverseEngaged") + " brakeLights " + Get<bool>("BrakeLightsOn") + " alarmPlaying " + Source("BackingAlarm").isPlaying + " alarmVol " + Source("BackingAlarm").volume + " whiteLit " + LampState("ReverseLedOn"));

        Shot("rear_reverse");

        Hold(Key.None);

        yield return new WaitForSeconds(2f);

        Say("stopped: speed " + Get<float>("SignedSpeed").ToString("F1") + " alarmVol " + Source("BackingAlarm").volume + " whiteLit " + LampState("ReverseLedOn"));

        // Face away from walls: turn around by reversing less; just go forward now.
        Hold(Key.W);

        var top = 0f;

        for (var i = 0; i < 40; i++)
        {
            yield return new WaitForSeconds(0.1f);

            top = Mathf.Max(top, Get<float>("SignedSpeed"));

            if (i == 5)
            {
                Say("accel 0.6s: speed " + Get<float>("SignedSpeed").ToString("F1") + " turboVol " + Source("TurboWhine").volume.ToString("F2") + " highVol " + Source("EngineHigh").volume.ToString("F2"));
            }
        }

        Say("accel 4s: top speed " + top.ToString("F1") + " m/s (" + (top * 2.237f).ToString("F0") + " mph)");

        Hold(Key.Space);

        yield return new WaitForSeconds(0.3f);

        Say("braking: speed " + Get<float>("SignedSpeed").ToString("F1") + " brakeLights " + Get<bool>("BrakeLightsOn") + " redLit " + LampState("BrakeLedOn") + " squealVol " + Source("BrakeSqueal").volume.ToString("F2") + " airBrakePlaying " + Source("AirBrake").isPlaying + " alarmVol " + Source("BackingAlarm").volume);

        Shot("rear_brake");

        yield return new WaitForSeconds(2f);

        Hold(Key.None);

        Say("DONE");
    }

    private void Hold(Key key)
    {
        var keyboard = Keyboard.current;

        var state = new KeyboardState();

        if (key != Key.None)
        {
            state.Press(key);
        }

        InputSystem.QueueStateEvent(keyboard, state);
    }

    private T Get<T>(string property)
    {
        return (T)_response.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(_response);
    }

    private AudioSource Source(string sourceName)
    {
        foreach (var source in _truck.GetComponentsInChildren<AudioSource>(true))
        {
            if (source.name == sourceName)
            {
                return source;
            }
        }

        return null;
    }

    private bool LampState(string materialName)
    {
        foreach (var renderer in _truck.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.name == "LedDot" && renderer.sharedMaterial != null && renderer.sharedMaterial.name == materialName)
            {
                return true;
            }
        }

        return false;
    }

    private void Shot(string name)
    {
        var go = new GameObject("RearCam");

        var cam = go.AddComponent<Camera>();

        cam.transform.position = _truck.TransformPoint(new Vector3(0.6f, 1.6f, -8.5f));

        cam.transform.LookAt(_truck.TransformPoint(new Vector3(0f, 1.2f, -4.5f)));

        cam.fieldOfView = 40f;

        var rt = new RenderTexture(1200, 700, 24);

        cam.targetTexture = rt;

        cam.Render();

        RenderTexture.active = rt;

        var tex = new Texture2D(1200, 700, TextureFormat.RGB24, false);

        tex.ReadPixels(new Rect(0, 0, 1200, 700), 0, 0);

        tex.Apply();

        File.WriteAllBytes(VerifyDriving.ShotFolder + name + ".png", tex.EncodeToPNG());

        RenderTexture.active = null;

        cam.targetTexture = null;

        Destroy(rt);

        Destroy(tex);

        Destroy(go);
    }

    private void Say(string line)
    {
        _log.AppendLine(line);

        File.WriteAllText(VerifyDriving.LogPath, _log.ToString());
    }
}
