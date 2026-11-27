#nullable enable
using System;
using System.Collections.Generic;
using HTownSim.Runtime.Incidents.Models;

namespace HTownSim.Runtime
{
    /// <summary>
    /// What the active-incidents board on the HUD shows, wherever it comes from: the city's own live feed (single player and the city
    /// server) or the board the city server streams to a player (<see cref="RemoteIncidentBoard"/>). The HUD only reads this.
    /// </summary>
    internal interface IIncidentBoard
    {
        /// <summary>The incidents on the board, newest first.</summary>
        IReadOnlyList<LiveBoardEntry> Board { get; }

        /// <summary>How many incidents wait to be released onto the board.</summary>
        int Waiting { get; }

        /// <summary>True while the live City feed is delivering incidents.</summary>
        bool IsLive { get; }

        /// <summary>How many incident lines the HUD shows.</summary>
        int HudLines { get; }

        /// <summary>The board changed: an incident came, was updated or closed.</summary>
        event Action? Changed;
    }
}
