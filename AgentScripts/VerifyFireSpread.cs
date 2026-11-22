using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Live Play Mode checks for fire growth, spread and smoke intensity. Drives FireSpread.Tick with simulated time.</summary>
public static class VerifyFireSpread
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>Runs ignition, growth, spread, suppression and reset assertions.</summary>
    public static string Run()
    {
        if (!Application.isPlaying)
        {
            throw new InvalidOperationException("Enter Play Mode first.");
        }

        var truck = GameObject.Find("Engine07");

        var response = truck.GetComponent("FireResponse");

        var spread = truck.GetComponent("FireSpread");

        var fire = Field<ParticleSystem>(response, "_fire");

        var smoke = Field<ParticleSystem>(response, "_smoke");

        var results = new List<string>();

        response.GetType().GetMethod("Restart", Flags).Invoke(response, null);

        Check(results, Prop<float>(spread, "Growth") == 1f, "Fire starts at growth 1");

        Check(results, Prop<int>(spread, "SpreadCount") == 0, "No satellites at ignition");

        var startFlameRate = fire.emission.rateOverTime.constant;

        var startSmokeRate = smoke.emission.rateOverTime.constant;

        var startSmokeSize = smoke.main.startSizeMultiplier;

        var startSmokeColor = smoke.main.startColor.color;

        Tick(spread, 30f);

        var mid = Prop<float>(spread, "Growth");

        Check(results, mid > 1.5f && mid < 2f, "30 s of burning grows the fire (" + mid + "x)");

        Check(results, Prop<int>(spread, "SpreadCount") >= 1, "Fire has spread to a neighbouring bay (" + Prop<int>(spread, "SpreadCount") + ")");

        Tick(spread, 100f);

        Check(results, Prop<float>(spread, "Growth") == 3f, "Growth caps at 3x");

        Check(results, Prop<int>(spread, "SpreadCount") == 4, "All four satellites burning");

        Check(results, fire.emission.rateOverTime.constant > startFlameRate * 2f, "Main flames more intense (" + fire.emission.rateOverTime.constant + "/s)");

        Check(results, smoke.emission.rateOverTime.constant > startSmokeRate * 3f, "Smoke rate intensified (" + smoke.emission.rateOverTime.constant + "/s)");

        Check(results, smoke.main.startSizeMultiplier > startSmokeSize * 1.5f, "Smoke puffs larger");

        Check(results, smoke.main.startColor.color.r < startSmokeColor.r * 0.5f, "Smoke darker");

        var satellite = GameObject.Find("Flames" + "Spread0");

        Check(results, satellite != null && satellite.GetComponent<ParticleSystem>().isPlaying, "Satellite fire is playing");

        Check(results, Vector3.Distance(satellite.transform.position, fire.transform.position) > 3f, "Satellite sits beside the main fire");

        var heatBefore = (float)response.GetType().GetProperty("Heat", Flags).GetValue(response);

        response.GetType().GetMethod("Suppress", Flags).Invoke(response, new object[] { 0f });

        Check(results, heatBefore == 1f, "Heat untouched by growth");

        Tick(spread, 10f, true);

        Check(results, Prop<float>(spread, "Growth") == 3f, "Growth holds while wetted");

        response.GetType().GetMethod("Restart", Flags).Invoke(response, null);

        Check(results, Prop<float>(spread, "Growth") == 1f && Prop<int>(spread, "SpreadCount") == 0, "Restart resets growth and spread");

        Check(results, !satellite.GetComponent<ParticleSystem>().isPlaying, "Satellites out after reset");

        Check(results, Mathf.Approximately(fire.emission.rateOverTime.constant, startFlameRate), "Flame rate back to baseline");

        Check(results, Mathf.Approximately(smoke.emission.rateOverTime.constant, startSmokeRate), "Smoke rate back to baseline");

        return string.Join("\n", results) + "\nAll fire spread checks passed.";
    }

    private static void Tick(Component spread, float seconds, bool wetted = false)
    {
        var tick = spread.GetType().GetMethod("Tick", Flags);

        for (var i = 0; i < (int)seconds; i++)
        {
            tick.Invoke(spread, new object[] { 1f, 1f, wetted });
        }
    }

    private static T Field<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, Flags).GetValue(target);
    }

    private static T Prop<T>(object target, string name)
    {
        return (T)target.GetType().GetProperty(name, Flags).GetValue(target);
    }

    private static void Check(List<string> results, bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAIL: " + label);
        }

        results.Add("PASS: " + label);
    }
}
