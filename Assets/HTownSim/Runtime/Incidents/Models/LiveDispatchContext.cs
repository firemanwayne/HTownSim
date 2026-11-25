#nullable enable
using System.Collections.Generic;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>What the dispatch rule needs to know about the player: their unit codes, the kind of rig, and their station.</summary>
    internal sealed class LiveDispatchContext
    {
        /// <summary>Creates a context.</summary>
        internal LiveDispatchContext(IReadOnlyList<string> unitCodes, LiveUnitKind kind, int stationIndex)
        {
            UnitCodes = unitCodes;

            Kind = kind;

            StationIndex = stationIndex;
        }

        /// <summary>The HFD unit codes that mean the player (e.g. E007; a chief has a few spellings).</summary>
        internal IReadOnlyList<string> UnitCodes { get; }

        /// <summary>What kind of rig the player drives.</summary>
        internal LiveUnitKind Kind { get; }

        /// <summary>The player's station, as an index into the station list the rule is given.</summary>
        internal int StationIndex { get; }
    }
}
