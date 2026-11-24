using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Live Play Mode check of the expanded city. Run Start, wait about 90 seconds, then run Report. The engine is moved to
/// several spots (Station 8, the north end, the west strip, the old city) and traffic is sampled: how many cars are active,
/// how many overlap a building (a car off its street), and how many sit on a street axis. Results go to Temp/verify_expansion.log.
/// </summary>
public static class VerifyExpansion
{
    /// <summary>Where the driver writes its findings.</summary>
    public const string LogPath = "J:/Unity/projects/HTownSim/Temp/verify_expansion.log";

    /// <summary>Starts the scripted visits.</summary>
    public static string Start()
    {
        if (!Application.isPlaying)
        {
            throw new InvalidOperationException("Enter Play Mode first.");
        }

        File.Delete(LogPath);

        new GameObject("ExpansionTestDriver").AddComponent<ExpansionTestDriver>();

        return "Started; run Report in about 90 seconds.";
    }

    /// <summary>Returns the log written by the driver.</summary>
    public static string Report()
    {
        return File.Exists(LogPath) ? File.ReadAllText(LogPath) : "No log yet.";
    }
}

/// <summary>Moves the engine around the city and samples the traffic around it.</summary>
public sealed class ExpansionTestDriver : MonoBehaviour
{
    private readonly StringBuilder _log = new StringBuilder();

    private GameObject _truck = null!;

    private Transform _traffic = null!;

    private void Start()
    {
        _truck = GameObject.Find("Engine07");

        _traffic = GameObject.Find("Traffic").transform;

        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        yield return new WaitForSeconds(1f);

        Time.timeScale = 3f;

        var spots = new List<(string Name, Vector3 Position)>
        {
            ("Station 8 (Louisiana St)", new Vector3(-724f, 0.3f, 1060f)),
            ("North end (Leeland St x San Jacinto)", new Vector3(-215f, 0.3f, 1440f)),
            ("West strip (Milam St)", new Vector3(-622f, 0.3f, 250f)),
            ("Old city (Elgin St)", new Vector3(12f, 0.3f, -2f)),
        };

        foreach (var spot in spots)
        {
            Teleport(spot.Position);

            yield return new WaitForSeconds(4f);

            var samples = 0;

            var carSamples = 0;

            var inBuilding = 0;

            var pedSamples = 0;

            var examples = new List<string>();

            for (var i = 0; i < 40; i++)
            {
                yield return new WaitForSeconds(0.4f);

                samples++;

                foreach (Transform child in _traffic)
                {
                    if (!child.gameObject.activeSelf)
                    {
                        continue;
                    }

                    if (child.name.StartsWith("CarTemplate", StringComparison.Ordinal))
                    {
                        carSamples++;

                        if (OverlapsBuilding(child, out var what) && examples.Count < 4)
                        {
                            examples.Add(child.position.ToString("F0") + " in " + what);
                        }

                        if (OverlapsBuilding(child, out _))
                        {
                            inBuilding++;
                        }
                    }
                    else if (child.name.StartsWith("PedestrianTemplate", StringComparison.Ordinal))
                    {
                        pedSamples++;
                    }
                }
            }

            _log.AppendLine(spot.Name + ": avg active cars " + (carSamples / (float)samples).ToString("F1") + ", avg pedestrians " + (pedSamples / (float)samples).ToString("F1") + ", car samples inside a building " + inBuilding + " of " + carSamples + (examples.Count > 0 ? " e.g. " + string.Join("; ", examples) : string.Empty));

            File.WriteAllText(VerifyExpansion.LogPath, _log.ToString());
        }

        Time.timeScale = 1f;

        _log.AppendLine("DONE");

        File.WriteAllText(VerifyExpansion.LogPath, _log.ToString());
    }

    private void Teleport(Vector3 position)
    {
        var controller = _truck.GetComponent<CharacterController>();

        controller.enabled = false;

        _truck.transform.position = position;

        controller.enabled = true;
    }

    private static bool OverlapsBuilding(Transform car, out string what)
    {
        var hits = Physics.OverlapBox(car.position + Vector3.up * 1.0f, new Vector3(0.8f, 0.45f, 1.9f), car.rotation, ~0, QueryTriggerInteraction.Ignore);

        foreach (var hit in hits)
        {
            if (hit.transform.IsChildOf(car))
            {
                continue;
            }

            for (var t = hit.transform; t != null; t = t.parent)
            {
                if (t.name.StartsWith("Buildings", StringComparison.Ordinal) || t.name.StartsWith("FireStation8", StringComparison.Ordinal) || t.name.StartsWith("Station07", StringComparison.Ordinal))
                {
                    what = hit.name + " under " + t.name;

                    return true;
                }
            }
        }

        what = string.Empty;

        return false;
    }
}
