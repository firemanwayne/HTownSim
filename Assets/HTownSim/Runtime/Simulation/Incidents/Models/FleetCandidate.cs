#nullable enable
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>A unit that could be sent to an incident: who it is, what kind of rig, where it is now and whether it is free.</summary>
    internal sealed class FleetCandidate
    {
        /// <summary>Creates a candidate.</summary>
        internal FleetCandidate(string name, LiveUnitKind kind, Vector2 position, bool busy, bool isPlayer)
        {
            Name = name;

            Kind = kind;

            Position = position;

            Busy = busy;

            IsPlayer = isPlayer;
        }

        /// <summary>The unit's name, e.g. Engine 16.</summary>
        internal string Name { get; }

        /// <summary>The kind of rig.</summary>
        internal LiveUnitKind Kind { get; }

        /// <summary>Where the unit is now (ground x, z), or where it is parked.</summary>
        internal Vector2 Position { get; }

        /// <summary>True when the unit is already on a call.</summary>
        internal bool Busy { get; }

        /// <summary>True when a person is driving this unit.</summary>
        internal bool IsPlayer { get; }
    }
}
