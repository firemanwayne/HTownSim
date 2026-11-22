using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Live Play Mode checks for wheel roll and front steering. Run Drive, wait a moment, then Check.</summary>
public static class VerifyWheels
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>Restarts, then drives forward while turning right every frame for 4 seconds via a temporary driver.</summary>
    public static string Drive()
    {
        Truck(out var response);

        response.GetType().GetMethod("Restart", Flags).Invoke(response, null);

        new GameObject("WheelTestDriver").AddComponent<WheelTestDriver>().Response = response;

        return "Driving right-forward for 4 s; run Check within that window.";
    }

    /// <summary>Asserts the wheels rolled and the front wheels steered right while the rears stayed straight.</summary>
    public static string Check()
    {
        var truck = Truck(out _);

        var spin = truck.GetComponent("WheelSpin");

        var results = new List<string>();

        Assert(results, spin != null, "WheelSpin attached to the engine");

        Assert(results, Prop<int>(spin, "WheelCount") == 4, "Four wheels found");

        var roll = Prop<float>(spin, "Roll");

        Assert(results, roll > 1f && roll < 359f, "Wheels have rolled forward (" + roll.ToString("F1") + " deg)");

        var frontLeft = Find(truck, "WheelFrontLeft");

        var rearLeft = Find(truck, "WheelRearLeft");

        var frontRight = Find(truck, "WheelFrontRight");


        var steer = Prop<float>(spin, "Steer");

        Assert(results, steer > 1f, "Front wheels steer right (" + steer.ToString("F1") + " deg)");

        Assert(results, Quaternion.Angle(rearLeft.localRotation, Quaternion.Euler(roll, 0f, 0f)) < 0.5f, "Rear wheels only roll (no steer)");

        Assert(results, Quaternion.Angle(frontLeft.localRotation, frontRight.localRotation) < 0.01f, "Front pair steer together");

        Assert(results, Quaternion.Angle(rearLeft.localRotation, Find(truck, "WheelRearRight").localRotation) < 0.01f, "Left and right wheels roll in step");

        return string.Join("\n", results) + "\nAll wheel checks passed.";
    }

    private static GameObject Truck(out Component response)
    {
        if (!Application.isPlaying)
        {
            throw new InvalidOperationException("Enter Play Mode first.");
        }

        var truck = GameObject.Find("Engine07");

        response = truck.GetComponent("FireResponse");

        return truck;
    }

    private static Transform Find(GameObject truck, string name)
    {
        foreach (var t in truck.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
            {
                return t;
            }
        }

        throw new InvalidOperationException("Missing " + name);
    }

    private static T Prop<T>(object target, string name)
    {
        return (T)target.GetType().GetProperty(name, Flags).GetValue(target);
    }

    private static void Assert(List<string> results, bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAIL: " + label);
        }

        results.Add("PASS: " + label);
    }
}

/// <summary>Temporary per-frame driver used only by VerifyWheels.</summary>
public sealed class WheelTestDriver : MonoBehaviour
{
    public Component Response = null!;

    private float _left = 4f;

    private void Update()
    {
        _left -= Time.deltaTime;

        if (_left <= 0f)
        {
            Destroy(gameObject);

            return;
        }

        Response.GetType().GetMethod("Drive", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(Response, new object[] { new Vector2(1, 1), false });
    }
}
