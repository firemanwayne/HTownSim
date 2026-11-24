#nullable enable
using System.Collections;
using System.IO;
using System.Text;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Play Mode check of mouse-wheel zoom on the chase camera. Sends real wheel notches through the Input System and
/// checks: scrolling up moves the camera in, then narrows the lens once the camera is at its closest (about 6 m);
/// scrolling down moves it out to the far limit (4x, about 61 m); orbiting keeps the zoom; reset restores the default
/// framing and lens. Start via VerifyZoom.Start, read via VerifyZoom.Report about 10 seconds later.
/// Log: Temp/verify_zoom.log.
/// </summary>
public static class VerifyZoom
{
    public const string LogPath = "J:/Unity/projects/HTownSim/Temp/verify_zoom.log";

    public static string Start()
    {
        File.Delete(LogPath);

        new GameObject("ZoomTestDriver").AddComponent<ZoomTestDriver>();

        return "Started; Report in about 10 seconds.";
    }

    public static string Report()
    {
        return File.Exists(LogPath) ? File.ReadAllText(LogPath) : "No log yet.";
    }
}

/// <summary>Scrolls the wheel and samples the follow offset and lens.</summary>
public sealed class ZoomTestDriver : MonoBehaviour
{
    private IEnumerator Start()
    {
        var log = new StringBuilder();

        var follow = FindAnyObjectByType<CinemachineFollow>();

        var cam = follow.GetComponent<CinemachineCamera>();

        var response = GameObject.Find("Engine07").GetComponent("FireResponse");

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

        var initial = (Vector3)response.GetType().GetField("_initialCameraOffset", flags).GetValue(response);

        var fov = cam.Lens.FieldOfView;

        float Distance() => follow.FollowOffset.magnitude;

        void Check(bool ok, string label) => log.AppendLine((ok ? "PASS: " : "FAIL: ") + label);

        yield return new WaitForSecondsRealtime(0.5f);

        log.AppendLine(Application.isFocused ? "Input: real wheel events (game window focused)" : "Input: ZoomCamera directly (Editor in the background, wheel ignored by design)");

        Check(Mathf.Abs(Distance() - initial.magnitude) < 0.2f, $"Camera starts at the default distance ({Distance():F1} m)");

        // A few notches up: closer, lens unchanged.
        yield return Scroll(120f, 2);

        Check(Distance() < initial.magnitude * 0.85f && Mathf.Approximately(cam.Lens.FieldOfView, fov), $"Scrolling up moves the camera in ({Distance():F1} m, lens {cam.Lens.FieldOfView:F0} deg)");

        // Many more notches up: camera stops at its closest, the lens narrows instead.
        yield return Scroll(120f, 20);

        Check(Distance() > 5.5f && Distance() < 6.5f, $"Camera stops clear of the truck ({Distance():F1} m)");

        Check(cam.Lens.FieldOfView < fov * 0.55f, $"Past that the lens zooms in ({cam.Lens.FieldOfView:F0} deg of {fov:F0})");

        // Far out.
        yield return Scroll(-120f, 30);

        Check(Mathf.Abs(cam.Lens.FieldOfView - fov) < 0.1f, $"Lens returns to normal when zooming out ({cam.Lens.FieldOfView:F0} deg)");

        Check(Mathf.Abs(Distance() - initial.magnitude * 4f) < 0.5f, $"Zoom out stops at the far limit ({Distance():F1} m)");

        var far = Distance();

        response.GetType().GetMethod("OrbitCamera", flags)!.Invoke(response, new object[] { new Vector2(200f, 40f) });

        Check(Mathf.Abs(Distance() - far) < 0.05f, $"Orbiting keeps the zoom ({Distance():F1} m)");

        response.GetType().GetMethod("ResetCamera", flags)!.Invoke(response, null);

        yield return null;

        Check(Vector3.Distance(follow.FollowOffset, initial) < 0.01f && Mathf.Abs(cam.Lens.FieldOfView - fov) < 0.1f, "Reset restores the default framing and lens");

        log.AppendLine("DONE");

        File.WriteAllText(VerifyZoom.LogPath, log.ToString());

        Destroy(gameObject);
    }

    /// <summary>
    /// Sends wheel notches one per frame (scroll is a per-frame delta), then waits for the camera to settle. FireResponse
    /// ignores the wheel while the game window is not focused (so scrolling in another app never zooms), so when the
    /// Editor is in the background the notches go straight to FireResponse.ZoomCamera instead (the same code path after
    /// the wheel is read).
    /// </summary>
    private IEnumerator Scroll(float y, int notches)
    {
        var response = GameObject.Find("Engine07").GetComponent("FireResponse");

        var zoom = response.GetType().GetMethod("ZoomCamera", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        for (var i = 0; i < notches; i++)
        {
            if (Application.isFocused)
            {
                InputSystem.QueueStateEvent(Mouse.current, new MouseState { scroll = new Vector2(0f, y) });
            }
            else
            {
                zoom.Invoke(response, new object[] { y / 120f });
            }

            yield return null;

            yield return null;
        }

        yield return new WaitForSecondsRealtime(1.2f);
    }
}
