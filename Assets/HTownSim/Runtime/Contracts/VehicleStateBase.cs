#nullable enable
using HTownSim.Runtime.Contracts.Interfaces;
using UnityEngine;

namespace HTownSim.Runtime.Contracts
{
    /// <summary>Stable vehicle state consumed by unit equipment and world systems.</summary>
    internal abstract class VehicleStateBase : MonoBehaviour
    {
        /// <summary>The active apparatus dimensions and equipment.</summary>
        internal abstract ApparatusProfileValues Profile { get; }

        /// <summary>Whether the aerial hydraulics are moving.</summary>
        internal abstract bool AerialHydraulicsWorking { get; }

        /// <summary>Whether player simulation is paused.</summary>
        internal abstract bool IsPaused { get; }

        /// <summary>Whether emergency lamps are currently active.</summary>
        internal abstract bool LightsActive { get; }

        /// <summary>Whether the left turn signal is active.</summary>
        internal abstract bool LeftSignalOn { get; }

        /// <summary>Whether the right turn signal is active.</summary>
        internal abstract bool RightSignalOn { get; }

        /// <summary>Whether the headlights are active.</summary>
        internal abstract bool HeadlightsActive { get; }

        /// <summary>Whether the siren is active.</summary>
        internal abstract bool SirenActive { get; }

        /// <summary>The unsigned driving speed.</summary>
        internal abstract float Speed { get; }

        /// <summary>The signed driving speed.</summary>
        internal abstract float SignedSpeed { get; }

        /// <summary>Whether dispatch audio has priority.</summary>
        internal abstract bool DispatchPlaying { get; }

        /// <summary>Whether engine audio is muted.</summary>
        internal abstract bool EngineMuted { get; }

        /// <summary>Whether the scene lights are switched on.</summary>
        internal abstract bool SceneLightsOn { get; }

        /// <summary>The current throttle input.</summary>
        internal abstract float ThrottleInput { get; }

        /// <summary>Whether the brake lamps are illuminated.</summary>
        internal abstract bool BrakeLightsOn { get; }

        /// <summary>Whether reverse is engaged.</summary>
        internal abstract bool ReverseEngaged { get; }

        /// <summary>Whether the driver is braking.</summary>
        internal abstract bool BrakeInput { get; }

        /// <summary>The world's collision handler, registered when traffic starts.</summary>
        internal abstract ITrafficInteractions? Traffic { get; set; }

        /// <summary>Scales vehicle speed after a traffic impact.</summary>
        internal abstract void ScaleSpeed(float factor);
    }
}
