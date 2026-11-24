#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Play Mode audio probe: puts Engine 7 on Milam St facing north-east, holds W for 9 seconds, then lets go, and logs
/// every audible AudioSource in the scene (playing, not muted: name, volume, pitch) every 0.1 s. Start via ProbeDrivingAudio.Start, read the
/// summary via ProbeDrivingAudio.Report (about 16 seconds later). Log: Temp/probe_audio.log.
/// </summary>
public static class ProbeDrivingAudio
{
    /// <summary>Where the probe writes.</summary>
    public const string LogPath = "J:/Unity/projects/HTownSim/Temp/probe_audio.log";

    /// <summary>Starts the scripted drive: W for 9 s, then coast.</summary>
    public static string Start()
    {
        return Begin(Key.None);
    }

    /// <summary>W for 5 s, then Space (brake) for 5 s, then nothing.</summary>
    public static string StartBrakeSpace()
    {
        return Begin(Key.Space);
    }

    /// <summary>W for 5 s, then S (throttle against travel, then reverse) for 5 s, then nothing.</summary>
    public static string StartBrakeS()
    {
        return Begin(Key.S);
    }

    private static string Begin(Key brakeKey)
    {
        File.Delete(LogPath);

        new GameObject("AudioProbeDriver").AddComponent<AudioProbeDriver>().BrakeKey = brakeKey;

        return "Started; Report in about 16 seconds.";
    }

    /// <summary>Returns the log.</summary>
    public static string Report()
    {
        return File.Exists(LogPath) ? File.ReadAllText(LogPath) : "No log yet.";
    }
}

/// <summary>Drives and samples.</summary>
public sealed class AudioProbeDriver : MonoBehaviour
{
    /// <summary>Key held from 5 s to 10 s; Key.None keeps W held to 9 s instead.</summary>
    public Key BrakeKey = Key.None;

    private IEnumerator Start()
    {
        var truck = GameObject.Find("Engine07").transform;

        var controller = truck.GetComponent<CharacterController>();

        controller.enabled = false;

        truck.position = new Vector3(-622f, 0.3f, 200f);

        truck.rotation = Quaternion.identity;

        controller.enabled = true;

        yield return new WaitForSeconds(2f);

        var response = truck.GetComponent("FireResponse");

        var speedProperty = response.GetType().GetProperty("SignedSpeed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;

        // Per source: list of (time, volume, pitch).
        var tracks = new Dictionary<string, List<(float T, float V, float P)>>();

        var speeds = new List<(float T, float S)>();

        Hold(Key.W);

        var start = Time.time;

        while (Time.time - start < 13f)
        {
            if (BrakeKey == Key.None)
            {
                if (Time.time - start > 9f)
                {
                    Hold(Key.None);
                }
            }
            else if (Time.time - start > 10f)
            {
                Hold(Key.None);
            }
            else if (Time.time - start > 5f)
            {
                Hold(BrakeKey);
            }

            var t = Time.time - start;

            speeds.Add((t, (float)speedProperty.GetValue(response)));

            foreach (var source in FindObjectsByType<AudioSource>())
            {
                if (!source.isPlaying || source.volume < 0.01f || source.mute)
                {
                    continue;
                }

                var key = Path(source.transform) + (source.clip != null ? " [" + source.clip.name + "]" : "");

                if (!tracks.TryGetValue(key, out var list))
                {
                    list = new List<(float, float, float)>();

                    tracks[key] = list;
                }

                list.Add((t, source.volume, source.pitch));
            }

            yield return new WaitForSeconds(0.1f);
        }

        Hold(Key.None);

        var log = new StringBuilder();

        log.AppendLine("speed m/s by second: " + string.Join(" ", Enumerable.Range(0, 13).Select(s => speeds.Where(x => x.T >= s && x.T < s + 1).Select(x => x.S).DefaultIfEmpty(0f).Average().ToString("F1"))));

        foreach (var pair in tracks.OrderByDescending(p => p.Value.Max(x => x.V)))
        {
            var list = pair.Value;

            // Direction changes of pitch while held: counts the high-low swings.
            var swings = 0;

            var lastDir = 0;

            for (var i = 1; i < list.Count; i++)
            {
                var d = list[i].P - list[i - 1].P;

                var dir = d > 0.01f ? 1 : d < -0.01f ? -1 : 0;

                if (dir != 0 && lastDir != 0 && dir != lastDir)
                {
                    swings++;
                }

                if (dir != 0)
                {
                    lastDir = dir;
                }
            }

            var pitchBySecond = string.Join(" ", Enumerable.Range(0, 13).Select(s => list.Where(x => x.T >= s && x.T < s + 1).Select(x => x.P).DefaultIfEmpty(0f).Average().ToString("F2")));

            log.AppendLine($"{pair.Key}: samples {list.Count}, vol max {list.Max(x => x.V):F2} avg {list.Average(x => x.V):F2}, pitch {list.Min(x => x.P):F2}..{list.Max(x => x.P):F2}, pitch swings {swings}");

            log.AppendLine($"    pitch by second: {pitchBySecond}");
        }

        log.AppendLine("DONE");

        File.WriteAllText(ProbeDrivingAudio.LogPath, log.ToString());

        Destroy(gameObject);
    }

    private static string Path(Transform t)
    {
        return t.parent == null ? t.name : t.parent.name + "/" + t.name;
    }

    private static void Hold(Key key)
    {
        var state = new KeyboardState();

        if (key != Key.None)
        {
            state.Press(key);
        }

        InputSystem.QueueStateEvent(Keyboard.current, state);
    }
}
