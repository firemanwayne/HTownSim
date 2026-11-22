using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Live Play Mode checks for EnvironmentWeather. Weather blends over 5 s, so run Begin, wait ~7 s, then Finish.</summary>
public static class VerifyWeather
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>Finds the controller and asserts the Clear baseline, then switches to Rain.</summary>
    public static string Begin()
    {
        var weather = Controller();

        var results = new List<string>();

        Check(results, Read<float>(weather, "_rainAmount") < 0.001f, "Starts with no rain");

        Check(results, Read<float>(weather, "_cloud") < 0.001f, "Starts with no cloud");

        Check(results, Read<float>(GameObject.Find("DuskSun").GetComponent<Light>(), "intensity") > 0.1f, "Sun lit at start");

        weather.GetType().GetMethod("SetWeather", Flags).Invoke(weather, new object[] { Enum.ToObject(weather.GetType().GetProperty("Preset", Flags).PropertyType, 2) });

        return string.Join("\n", results) + "\nSwitched to Rain; wait ~7 s then run Finish.";
    }

    /// <summary>Asserts the Rain end state: dimmer sun, fog, wet roads, audible rain, particles emitted.</summary>
    public static string Finish()
    {
        var weather = Controller();

        var results = new List<string>();

        var sun = GameObject.Find("DuskSun").GetComponent<Light>();

        var baseIntensity = Read<float>(weather, "_sunIntensity");

        Check(results, Read<float>(weather, "_rainAmount") > 0.99f, "Rain amount reached 1");

        Check(results, Read<float>(weather, "_cloud") > 0.99f, "Cloud amount reached 1");

        Check(results, sun.intensity < baseIntensity * 0.6f, "Sun dimmed under rain (" + sun.intensity + " of " + baseIntensity + ")");

        Check(results, RenderSettings.fog && RenderSettings.fogMode == FogMode.ExponentialSquared, "Fog enabled");

        var rain = Read<ParticleSystem>(weather, "_rain");

        Check(results, rain.isPlaying && rain.particleCount > 0, "Rain particles alive (" + rain.particleCount + ")");

        var rainAudio = Read<AudioSource>(weather, "_rainAudio");

        Check(results, rainAudio.isPlaying && rainAudio.volume > 0.01f, "Rain audio audible (" + rainAudio.volume + ")");

        var roads = Read<Renderer[]>(weather, "_roads");

        Check(results, roads.Length > 0, "Wet-road renderers wired (" + roads.Length + ")");

        var block = new MaterialPropertyBlock();

        roads[0].GetPropertyBlock(block);

        Check(results, block.GetFloat("_Smoothness") > 0.7f, "Road smoothness raised");

        weather.GetType().GetMethod("SetWeather", Flags).Invoke(weather, new object[] { Enum.ToObject(weather.GetType().GetProperty("Preset", Flags).PropertyType, 0) });

        return string.Join("\n", results) + "\nAll weather checks passed; reset to Clear.";
    }

    private static Component Controller()
    {
        if (!Application.isPlaying)
        {
            throw new InvalidOperationException("Enter Play Mode first.");
        }

        return GameObject.Find("EnvironmentWeather").GetComponent("EnvironmentWeather");
    }

    private static T Read<T>(object target, string name)
    {
        var type = target.GetType();

        var field = type.GetField(name, Flags);

        return field != null ? (T)field.GetValue(target) : (T)type.GetProperty(name, Flags).GetValue(target);
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
