#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>One apparatus's fit-out values (see <c>ApparatusProfile</c>).</summary>
    internal sealed class ApparatusProfileValues
    {
        internal ApparatusProfileValues(
            string unitName,
            float wheelRadius,
            float wheelbase,
            string[] frontWheelPrefixes,
            string[] rearWheelPrefixes,
            float turnRate,
            float cameraDistanceScale,
            Vector3 waterOrigin,
            Vector3 sceneLight,
            bool rearLamps,
            string[] flashGroupA,
            string[] flashGroupB,
            string[] steadyLenses,
            bool airHorn,
            bool canSprayWater = true,
            string lensHalos = "",
            bool emsAlert = false,
            bool emergencyEquipment = true,
            bool canTransport = false,
            string[]? seats = null)
        {
            CanTransport = canTransport;

            Seats = seats ?? new[] { "Driver" };

            UnitName = unitName;

            WheelRadius = wheelRadius;

            Wheelbase = wheelbase;

            FrontWheelPrefixes = frontWheelPrefixes;

            RearWheelPrefixes = rearWheelPrefixes;

            TurnRate = turnRate;

            CameraDistanceScale = cameraDistanceScale;

            WaterOrigin = waterOrigin;

            SceneLight = sceneLight;

            RearLamps = rearLamps;

            FlashGroupA = flashGroupA;

            FlashGroupB = flashGroupB;

            SteadyLenses = steadyLenses;

            AirHorn = airHorn;

            CanSprayWater = canSprayWater;

            LensHalos = lensHalos;

            EmsAlert = emsAlert;

            EmergencyEquipment = emergencyEquipment;
        }

        /// <summary>True when the unit takes patients to a hospital (an ambulance or medic unit). Any other unit leaves a medical call for its station from the scene.</summary>
        internal bool CanTransport { get; }

        /// <summary>The riding positions in seat order, the first being the driver; its length is how many people the unit carries (Engine: operator, officer, two firefighters).</summary>
        internal string[] Seats { get; }

        /// <summary>How this kind of vehicle flashes its lights, which siren tones it has and how it drives (one set for every unit of its kind).</summary>
        internal UnitBehavior Behavior => UnitBehavior.For(UnitName);

        /// <summary>False for a citizen car: no siren, lightbar or horn tones, just headlights, blinkers and the pedal lamps.</summary>
        internal bool EmergencyEquipment { get; }

        /// <summary>True for an EMS unit: a new call plays the EMS high/low alert tone instead of the fire station ring and beeps.</summary>
        internal bool EmsAlert { get; }

        /// <summary>Resource name of the lens glow list for a model whose lenses are part of its mesh. Set it to get camera-facing glows (bloom) on the lenses; empty means none.</summary>
        internal string LensHalos { get; }

        /// <summary>True lets E spray water at fires (engine and ladder); false for an ambulance or car. It does not decide who can transport patients (see UnitName).</summary>
        internal bool CanSprayWater { get; }

        /// <summary>True gives the unit the air horn (engine and ladder); false gives the plain electric horn (ambulance, cars).</summary>
        internal bool AirHorn { get; }

        /// <summary>The unit's radio name, e.g. "Ladder 07". It is shown in the HUD and the incident text, and decides which unit the dispatcher thinks the player is (Engine 16 answers E016) and whether the unit can transport patients (only names starting Ambulance or Medic can).</summary>
        internal string UnitName { get; }

        /// <summary>Tyre rolling radius, metres. It sets how fast the wheels turn for a given speed (a bigger radius turns slower), so set it to the tyre's real size or the wheels will look like they slip or spin too fast. It does not change how fast the unit drives.</summary>
        internal float WheelRadius { get; }

        /// <summary>Front axle to rear axle (or tandem centre), metres. Raise it and the front wheels are drawn turned further for the same turn (a longer truck needs more wheel angle for the same curve); it only changes how the wheels look, not the turning radius (TurnRate sets that). Use the model's real axle distance.</summary>
        internal float Wheelbase { get; }

        /// <summary>Name prefixes of the steering wheels. Parts whose name starts with one of these are the front wheels: they roll and turn with the steering. A wrong prefix leaves the wheels still.</summary>
        internal string[] FrontWheelPrefixes { get; }

        /// <summary>Name prefixes of the non-steering wheels. Parts whose name starts with one of these roll but do not steer.</summary>
        internal string[] RearWheelPrefixes { get; }

        /// <summary>Yaw in degrees per metre travelled at full steering lock. This is the turning radius: raise it for a tighter turn (a nimble patrol car, about 10), lower it for a wider one (a long ladder truck, about 2.5). The turning radius is roughly 57 divided by this value, in metres.</summary>
        internal float TurnRate { get; }

        /// <summary>Multiplies the chase camera's distance behind the unit. Raise it for a camera that sits further back (long ladder truck); lower it to bring the camera in close (a small car).</summary>
        internal float CameraDistanceScale { get; }

        /// <summary>Where the water stream starts, in the apparatus's local space (x right, y up, z forward). Move it to put the nozzle in the right place; wrong values make the stream come from the roof or the middle of the cab.</summary>
        internal Vector3 WaterOrigin { get; }

        /// <summary>Position of the right-hand scene light in local space (the left one is its mirror). Move it to put the work lights where the model's light heads are.</summary>
        internal Vector3 SceneLight { get; }

        /// <summary>Whether the add-on LED brake and reverse panels are built. Turn it off for a model that has its own tail lamps, or you get doubled lights.</summary>
        internal bool RearLamps { get; }

        /// <summary>Lens materials of the first flash group (the warning lights that flash together). Add a material name to make that lens flash; the second group takes the opposite half of each cycle.</summary>
        internal string[] FlashGroupA { get; }

        /// <summary>Lens materials of the second, alternating flash group. They light while the first group is dark.</summary>
        internal string[] FlashGroupB { get; }

        /// <summary>Lens materials that burn steadily with the headlights (headlamps, fog lamps). Add a material to make it glow whenever the headlights are on.</summary>
        internal string[] SteadyLenses { get; }
    }
}
