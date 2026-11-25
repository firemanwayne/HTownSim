using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Live Play Mode checks for siren yielding and collision physics. Run Start, wait about 25 seconds, then run Report.
/// The test plays out over real frames through a temporary driver component and writes its findings to Temp/verify_traffic.log.
/// </summary>
public static class VerifyTraffic
{
    /// <summary>Where the driver component writes results (read back by Report).</summary>
    public const string LogPath = "J:/Unity/projects/HTownSim/Temp/verify_traffic.log";

    /// <summary>Starts the scripted scenarios.</summary>
    public static string Start()
    {
        if (!Application.isPlaying)
        {
            throw new InvalidOperationException("Enter Play Mode first.");
        }

        File.Delete(LogPath);

        new GameObject("TrafficTestDriver").AddComponent<TrafficTestDriver>();

        return "Started; run Report in about 25 seconds.";
    }

    /// <summary>Returns the log written by the driver.</summary>
    public static string Report()
    {
        return File.Exists(LogPath) ? File.ReadAllText(LogPath) : "No log yet.";
    }
}

/// <summary>Plays out the yielding, car-collision and pedestrian-collision scenarios and logs pass/fail lines.</summary>
public sealed class TrafficTestDriver : MonoBehaviour
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private readonly StringBuilder _log = new StringBuilder();

    private GameObject _truck = null!;

    private Component _response = null!;

    private Component _traffic = null!;

    private bool _driving;

    private void Start()
    {
        _truck = GameObject.Find("Engine07");

        _response = _truck.GetComponent("FireResponse");

        _traffic = FindAnyObjectByType(Type.GetType("HTownSim.Runtime.TrafficSystem, HTownSim.World")) as Component;

        StartCoroutine(Run());
    }

    private void Update()
    {
        if (_driving)
        {
            // The game's own per-frame Drive (no keys held) would cancel extra Drive calls, so hold the speed directly.
            _response.GetType().GetField("_speed", Flags).SetValue(_response, 12f);
        }
    }

    private IEnumerator Run()
    {
        yield return new WaitForSeconds(1f);

        Call("Restart");

        // ---- Yielding: baseline with the siren off, then on.
        yield return new WaitForSeconds(5f);

        var baseline = Yielders();

        SetSiren(true);

        yield return new WaitForSeconds(7f);

        var withSiren = Yielders();

        Log(withSiren.Total >= 2, "Enough cars in the yield zone to test (" + withSiren.Total + ")");

        Log(withSiren.Stopped >= withSiren.Total * 0.7f, "Siren: cars that should yield are stopped (" + withSiren.Stopped + "/" + withSiren.Total + "; siren off had " + baseline.Stopped + "/" + baseline.Total + " stopped)");

        Log(withSiren.Pulled >= withSiren.Total * 0.7f, "Siren: yielding cars pulled toward the curb (" + withSiren.Pulled + "/" + withSiren.Total + ", max pull " + withSiren.MaxPull.ToString("F1") + " m)");

        Log(withSiren.Stopped > baseline.Stopped || baseline.Total == 0, "Siren stops more cars than no siren");

        SetSiren(false);

        // ---- Car collision: ram a parked car at speed.
        Call("Restart");

        yield return new WaitForSeconds(0.5f);

        var car = FirstCar(c => true);

        var carBody = (Rigidbody)Get(car, "Rb");

        var heading = ((Transform)Get(car, "Transform")).forward;

        Set(car, "Cruise", 0f);

        Set(car, "Speed", 0f);

        var carPosition = ((Transform)Get(car, "Transform")).position;

        Teleport(carPosition - heading * 16f + Vector3.up * 0.15f, Quaternion.LookRotation(heading));

        yield return null;

        _driving = true;

        var maxCarSpeed = 0f;

        var truckBefore = 0f;

        var truckAfter = float.MaxValue;

        var wasKnocked = false;

        for (var t = 0f; t < 4f; t += Time.deltaTime)
        {
            var speed = Mathf.Abs((float)_response.GetType().GetProperty("SignedSpeed", Flags).GetValue(_response));

            if (!(bool)Get(car, "Knocked"))
            {
                truckBefore = Mathf.Max(truckBefore, speed);
            }
            else
            {
                // Stop forcing the speed so the engine's own post-impact speed shows up on the next frame.
                if (wasKnocked && truckAfter == float.MaxValue)
                {
                    truckAfter = speed;
                }

                wasKnocked = true;
            }

            // Push the engine up to speed, then let go 9 m out so the game's own impact handling shows in the speed.
            _driving = !wasKnocked && Vector3.Distance(_truck.transform.position, ((Transform)Get(car, "Transform")).position) > 9f;

            maxCarSpeed = Mathf.Max(maxCarSpeed, carBody.linearVelocity.magnitude);

            if (Mathf.FloorToInt(t * 4f) != Mathf.FloorToInt((t - Time.deltaTime) * 4f))
            {
                var lastHit = _response.GetType().GetField("_lastHit", Flags).GetValue(_response) as Collider;

                _log.AppendLine("  debug t=" + t.ToString("F2") + " truckSpeed=" + speed.ToString("F1") + " dist=" + Vector3.Distance(_truck.transform.position, ((Transform)Get(car, "Transform")).position).ToString("F1") + " carActive=" + Get(car, "Active") + " carSpeed=" + Get(car, "Speed") + " knocked=" + Get(car, "Knocked") + " lastHit=" + (lastHit != null ? lastHit.name : "none") + " traffic=" + (_response.GetType().GetProperty("Traffic", Flags).GetValue(_response) != null));
            }

            yield return null;
        }

        _driving = false;

        Log(wasKnocked, "Car hit at speed is knocked onto physics (truck reached " + truckBefore.ToString("F1") + " m/s)");

        Log(maxCarSpeed > 5f, "Struck car is thrown (peak " + maxCarSpeed.ToString("F1") + " m/s)");

        Log(truckAfter > 0.5f && truckAfter < truckBefore, "Truck keeps part of its speed but is slowed (" + truckAfter.ToString("F1") + " vs " + truckBefore.ToString("F1") + " m/s)");

        Log(Vector3.Distance(((Transform)Get(car, "Transform")).position, carPosition) > 3f, "Car ended up displaced from where it was struck");

        // ---- Pedestrian collision: put a walker in the road ahead of the engine.
        Call("Restart");

        yield return new WaitForSeconds(0.5f);

        var ped = FirstPedestrian();

        var axis = Get(ped, "Axis");

        var alongZ = (bool)Get(axis, "AlongZ");

        var fixedAt = (float)Get(axis, "Fixed");

        var pedAlong = (float)Get(ped, "Along");

        Set(ped, "Side", 0f);

        Set(ped, "Direction", 1);

        Set(ped, "Min", (float)Get(axis, "Min"));

        Set(ped, "Max", (float)Get(axis, "Max"));

        Set(ped, "Speed", 0.2f);

        var forward = alongZ ? Vector3.forward : Vector3.right;

        var pedPoint = alongZ ? new Vector3(fixedAt, 0.15f, pedAlong) : new Vector3(pedAlong, 0.15f, fixedAt);

        Teleport(pedPoint - forward * 20f, Quaternion.LookRotation(forward));

        yield return null;

        _driving = true;

        var launched = false;

        var peakHeight = 0f;

        var pedTransform = (Transform)Get(ped, "Transform");

        for (var t = 0f; t < 4f; t += Time.deltaTime)
        {
            if ((bool)Get(ped, "Knocked"))
            {
                launched = true;

                peakHeight = Mathf.Max(peakHeight, pedTransform.position.y);
            }

            yield return null;
        }

        _driving = false;

        Log(launched, "Pedestrian run down is knocked onto physics");

        Log(peakHeight > 0.8f, "Pedestrian is launched into the air (peak " + peakHeight.ToString("F1") + " m)");

        Log(pedTransform.GetComponent<Rigidbody>() != null || !pedTransform.gameObject.activeSelf, "Pedestrian is a physics body until recycled");

        yield return new WaitForSeconds(7f);

        Log(!(bool)Get(ped, "Knocked") && pedTransform.GetComponent<Rigidbody>() == null, "Struck pedestrian is cleaned up and recycled");

        _log.AppendLine("DONE");

        Directory.CreateDirectory(Path.GetDirectoryName(VerifyTraffic.LogPath));

        File.WriteAllText(VerifyTraffic.LogPath, _log.ToString());

        Destroy(gameObject);
    }

    /// <summary>Counts cars the siren should make yield: within 60 m and either behind the engine's path or very close.</summary>
    private (int Total, int Stopped, int Pulled, float MaxPull) Yielders()
    {
        var total = 0;

        var stopped = 0;

        var pulled = 0;

        var maxPull = 0f;

        foreach (var car in (IEnumerable)_traffic.GetType().GetField("_cars", Flags).GetValue(_traffic))
        {
            if (!(bool)Get(car, "Active") || (bool)Get(car, "Knocked"))
            {
                continue;
            }

            var tr = (Transform)Get(car, "Transform");

            var toTruck = _truck.transform.position - tr.position;

            toTruck.y = 0f;

            var distance = toTruck.magnitude;

            var shouldYield = distance < 60f && (Vector3.Dot(toTruck, tr.forward) < 0f || distance < 35f);

            if (!shouldYield)
            {
                continue;
            }

            total++;

            if ((float)Get(car, "Speed") < 0.6f)
            {
                stopped++;
            }

            var pull = (float)Get(car, "Pull");

            maxPull = Mathf.Max(maxPull, pull);

            if (pull >= 0.5f)
            {
                pulled++;
            }
        }

        return (total, stopped, pulled, maxPull);
    }

    private object FirstCar(Func<object, bool> filter)
    {
        foreach (var car in (IEnumerable)_traffic.GetType().GetField("_cars", Flags).GetValue(_traffic))
        {
            if ((bool)Get(car, "Active") && !(bool)Get(car, "Knocked") && filter(car) && Vector3.Distance(((Transform)Get(car, "Transform")).position, _truck.transform.position) > 40f)
            {
                return car;
            }
        }

        throw new InvalidOperationException("No active car found.");
    }

    private object FirstPedestrian()
    {
        foreach (var ped in (IEnumerable)_traffic.GetType().GetField("_pedestrians", Flags).GetValue(_traffic))
        {
            if ((bool)Get(ped, "Active") && !(bool)Get(ped, "Knocked") && Vector3.Distance(((Transform)Get(ped, "Transform")).position, _truck.transform.position) > 40f)
            {
                return ped;
            }
        }

        throw new InvalidOperationException("No active pedestrian found.");
    }

    private void Teleport(Vector3 position, Quaternion rotation)
    {
        var controller = _truck.GetComponent<CharacterController>();

        controller.enabled = false;

        _truck.transform.SetPositionAndRotation(position, rotation);

        controller.enabled = true;

        Physics.SyncTransforms();
    }

    private void SetSiren(bool on)
    {
        _response.GetType().GetField("_sirenEnabled", Flags).SetValue(_response, on);
    }

    private void Call(string method)
    {
        _response.GetType().GetMethod(method, Flags).Invoke(_response, null);
    }

    private void Log(bool pass, string label)
    {
        _log.AppendLine((pass ? "PASS: " : "FAIL: ") + label);
    }

    private static object Get(object target, string name)
    {
        return target.GetType().GetField(name, Flags).GetValue(target);
    }

    private static void Set(object target, string name, object value)
    {
        target.GetType().GetField(name, Flags).SetValue(target, value);
    }
}
