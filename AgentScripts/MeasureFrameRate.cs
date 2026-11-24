#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Frame-rate probe (Play Mode). <c>MeasureFrameRate.Start</c> parks Engine 7 at six spots around the city and, at
/// each, waits for traffic to settle and then records 6 seconds of frames: average fps, 1% low fps, worst frame and
/// the editor render stats (batches, SetPass calls, triangles). <c>MeasureFrameRate.Report</c> returns the log
/// (about 70 seconds after Start). Results go to Temp/measure_fps.log. Editor numbers, not a player build.
/// </summary>
public static class MeasureFrameRate
{
    /// <summary>Where the driver writes its results.</summary>
    public const string LogPath = "J:/Unity/projects/HTownSim/Temp/measure_fps.log";

    /// <summary>Adds the measuring driver.</summary>
    public static string Start()
    {
        if (!EditorApplication.isPlaying)
        {
            return "Enter Play Mode first.";
        }

        File.WriteAllText(LogPath, "");

        new GameObject("FrameRateProbe").AddComponent<FrameRateProbe>();

        return "Measuring; run MeasureFrameRate.Report in about 70 seconds.";
    }

    /// <summary>Returns the log written so far.</summary>
    public static string Report()
    {
        return File.Exists(LogPath) ? File.ReadAllText(LogPath) : "No log yet.";
    }
}

/// <summary>Moves the engine between spots and samples frame times with unscaled time.</summary>
public sealed class FrameRateProbe : MonoBehaviour
{
    /// <summary>Seconds to let traffic and streaming settle after a move.</summary>
    private const float SettleSeconds = 3f;

    /// <summary>Seconds of frames recorded per spot.</summary>
    private const float SampleSeconds = 6f;

    /// <summary>Spots on the street grid (scene metres).</summary>
    private static readonly (string Name, Vector3 Position)[] Spots =
    {
        ("Station 8 (Louisiana St)", new Vector3(-724f, 0.3f, 1060f)),
        ("North end (Leeland x San Jacinto)", new Vector3(-215f, 0.3f, 1440f)),
        ("West strip (Milam St)", new Vector3(-622f, 0.3f, 250f)),
        ("Old city (Elgin St)", new Vector3(12f, 0.3f, -2f)),
        ("Houses (Tuam St)", new Vector3(130f, 0.3f, 250f)),
        ("Tower (Fannin St)", new Vector3(-318.6f, 0.3f, 289f)),
    };

    private void Start()
    {
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        var log = new StringBuilder();

        var truck = GameObject.Find("Engine07");

        var all = new List<float>();

        foreach (var spot in Spots)
        {
            Teleport(truck, spot.Position);

            yield return new WaitForSecondsRealtime(SettleSeconds);

            var frames = new List<float>();

            var batches = 0L;

            var setPass = 0L;

            var triangles = 0L;

            using var batchRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");

            using var setPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");

            using var triangleRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");

            var end = Time.realtimeSinceStartup + SampleSeconds;

            while (Time.realtimeSinceStartup < end)
            {
                yield return null;

                frames.Add(Time.unscaledDeltaTime);

                batches += batchRecorder.LastValue;

                setPass += setPassRecorder.LastValue;

                triangles += triangleRecorder.LastValue;
            }

            all.AddRange(frames);

            log.AppendLine($"{spot.Name}: {Summary(frames)}, draw calls {batches / frames.Count}, SetPass {setPass / frames.Count}, triangles {triangles / frames.Count / 1000}k");

            File.WriteAllText(MeasureFrameRate.LogPath, log.ToString());
        }

        log.AppendLine($"ALL: {Summary(all)}");

        log.AppendLine("DONE");

        File.WriteAllText(MeasureFrameRate.LogPath, log.ToString());

        Destroy(gameObject);
    }

    /// <summary>Average fps, 1% low fps (from the slowest 1% of frames) and the worst frame in ms.</summary>
    private static string Summary(List<float> frames)
    {
        var sorted = frames.OrderByDescending(f => f).ToList();

        var slowest = sorted.Take(Mathf.Max(1, sorted.Count / 100)).Average();

        return $"avg {frames.Count / frames.Sum():F0} fps, 1% low {1f / slowest:F0} fps, worst {sorted[0] * 1000f:F0} ms ({frames.Count} frames)";
    }

    /// <summary>Moves the engine like VerifyExpansion does (controller off while moving).</summary>
    private static void Teleport(GameObject truck, Vector3 position)
    {
        var controller = truck.GetComponent<CharacterController>();

        if (controller != null)
        {
            controller.enabled = false;
        }

        truck.transform.position = position;

        // Same heading every run (north-east, along +Z) so each spot sees the same part of the city.
        truck.transform.rotation = Quaternion.identity;

        if (controller != null)
        {
            controller.enabled = true;
        }
    }
}
