#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Passive Play Mode audio log: for 20 seconds, every 0.1 s, records the player's throttle, brake, brake lights, reverse
/// and speed plus every audible source on Engine 07 (playing, unmuted, volume above 0.01). It does not drive or press
/// anything, so it can run while someone plays. Start via ListenAudio.Start, read via ListenAudio.Report.
/// </summary>
public static class ListenAudio
{
    /// <summary>Where the listener writes.</summary>
    public const string LogPath = "J:/Unity/projects/HTownSim/Temp/listen_audio.log";

    /// <summary>Starts listening.</summary>
    public static string Start()
    {
        File.Delete(LogPath);

        new GameObject("AudioListenerLog").AddComponent<AudioListenLog>();

        return "Listening for 20 seconds.";
    }

    /// <summary>Returns the log.</summary>
    public static string Report()
    {
        return File.Exists(LogPath) ? File.ReadAllText(LogPath) : "No log yet.";
    }
}

/// <summary>Samples and writes one line per sample where something on the truck is audible.</summary>
public sealed class AudioListenLog : MonoBehaviour
{
    private IEnumerator Start()
    {
        var truck = GameObject.Find("Engine07").transform;

        var response = truck.GetComponent("FireResponse");

        object? Get(string name)
        {
            return response.GetType().GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(response);
        }

        var log = new StringBuilder();

        var start = Time.realtimeSinceStartup;

        // Engine sounds that play all the time are left out so the line shows what changes.
        var always = new HashSet<string> { "EngineIdle", "EngineLow", "EngineHigh", "EngineRoad", "DieselSource" };

        while (Time.realtimeSinceStartup - start < 20f)
        {
            var audible = truck
                .GetComponentsInChildren<AudioSource>()
                .Where(s => s.isPlaying && !s.mute && s.volume > 0.01f && !always.Contains(s.name))
                .Select(s => $"{s.name}[{(s.clip != null ? s.clip.name : "-")}] v{s.volume:F2} p{s.pitch:F2}")
                .ToList();

            var line = $"{Time.realtimeSinceStartup - start:F1}s speed {(float)(Get("SignedSpeed") ?? 0f):F1} thr {(float)(Get("ThrottleInput") ?? 0f):F1} brake {Get("BrakeInput")} lights {Get("BrakeLightsOn")} rev {Get("ReverseEngaged")} | {string.Join(", ", audible)}";

            log.AppendLine(line);

            yield return new WaitForSecondsRealtime(0.1f);
        }

        log.AppendLine("DONE");

        File.WriteAllText(ListenAudio.LogPath, log.ToString());

        Destroy(gameObject);
    }
}
