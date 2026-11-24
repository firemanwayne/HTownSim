#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>One apparatus's fit-out values (see <see cref="ApparatusProfile"/>).</summary>
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
            bool emsAlert = false)
        {
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
        }

        /// <summary>True for an EMS unit: a new call plays the EMS high/low alert tone instead of the fire station ring and beeps.</summary>
        internal bool EmsAlert { get; }

        /// <summary>Resource name of the lens glow list (Resources/*.json) for models whose lenses are in the mesh; empty for none.</summary>
        internal string LensHalos { get; }

        /// <summary>True when the apparatus carries water (engine and ladder); the ambulance cannot spray.</summary>
        internal bool CanSprayWater { get; }

        /// <summary>True when the apparatus has the synthesized air horn (engine and ladder); false uses the plain electric horn (ambulance).</summary>
        internal bool AirHorn { get; }

        /// <summary>The unit's radio name, e.g. "Ladder 07".</summary>
        internal string UnitName { get; }

        /// <summary>Tyre rolling radius, metres.</summary>
        internal float WheelRadius { get; }

        /// <summary>Front axle to rear axle (or tandem centre), metres.</summary>
        internal float Wheelbase { get; }

        /// <summary>Name prefixes of the steering wheels.</summary>
        internal string[] FrontWheelPrefixes { get; }

        /// <summary>Name prefixes of the non-steering wheels.</summary>
        internal string[] RearWheelPrefixes { get; }

        /// <summary>Yaw degrees per metre travelled at full lock.</summary>
        internal float TurnRate { get; }

        /// <summary>Chase camera distance multiplier.</summary>
        internal float CameraDistanceScale { get; }

        /// <summary>Water stream origin, local space.</summary>
        internal Vector3 WaterOrigin { get; }

        /// <summary>Right-hand scene light position, local space (mirrored for the left).</summary>
        internal Vector3 SceneLight { get; }

        /// <summary>Whether the add-on LED brake and reverse panels are built.</summary>
        internal bool RearLamps { get; }

        /// <summary>Lens materials of the first flash group.</summary>
        internal string[] FlashGroupA { get; }

        /// <summary>Lens materials of the second, alternating flash group.</summary>
        internal string[] FlashGroupB { get; }

        /// <summary>Lens materials lit steadily with the headlights.</summary>
        internal string[] SteadyLenses { get; }
    }
}
