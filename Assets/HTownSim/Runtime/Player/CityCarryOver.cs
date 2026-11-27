#nullable enable
using System.Collections.Generic;
using System.Linq;
using HTownSim.Runtime.Incidents.Models;
using HTownSim.Runtime.Incidents.Sources;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// What the city keeps when the player switches unit. A switch reloads the scene, which would send every computer-driven unit back
    /// to its station and drop the fires and the record of who is on which incident. The menu saves that here just before the reload
    /// (<see cref="Capture"/>); the new scene's fleet, incident feed and fires take it back as they start, and the menu's step in
    /// <see cref="FireResponse"/> clears it once they have. The unit the player leaves carries on with its call as a computer-driven
    /// unit.
    /// </summary>
    internal static class CityCarryOver
    {
        /// <summary>A computer-driven unit that is out on a call: where it is, what it is doing and the route it is on.</summary>
        internal sealed class UnitState
        {
            internal string Name { get; set; } = "";

            internal Vector3 Position { get; set; }

            internal float Yaw { get; set; }

            internal AiUnit.Phase Phase { get; set; }

            internal string IncidentId { get; set; } = "";

            internal AiUnit.Call Call { get; set; }

            internal bool HasPatient { get; set; }

            internal float Timer { get; set; }

            internal float Cruise { get; set; }

            internal float DepartDelay { get; set; }

            internal Vector2[] Route { get; set; } = new Vector2[0];

            internal int Next { get; set; }
        }

        /// <summary>The unit the player was driving: where it stands and the call it was on, if any (an empty incident id means none).</summary>
        internal sealed class PlayerUnit
        {
            internal string Name { get; set; } = "";

            internal Vector3 Position { get; set; }

            internal float Yaw { get; set; }

            internal string IncidentId { get; set; } = "";

            internal Vector2 Target { get; set; }

            internal AiUnit.Call Call { get; set; }
        }

        /// <summary>A fire that was burning: its incident, where, how hot and how grown.</summary>
        internal sealed class FireState
        {
            internal string Id { get; set; } = "";

            internal Vector3 Position { get; set; }

            internal float Heat { get; set; }

            internal float Growth { get; set; }
        }

        /// <summary>True while a capture waits to be taken back by the next scene.</summary>
        internal static bool HasData { get; private set; }

        internal static List<UnitState> Units { get; } = new List<UnitState>();

        internal static List<FireState> Fires { get; } = new List<FireState>();

        internal static PlayerUnit? Player { get; private set; }

        /// <summary>The units on each matched incident, how many are still on it, and the incidents already matched (the feed's record).</summary>
        internal static Dictionary<string, string[]> Assigned { get; } = new Dictionary<string, string[]>();

        internal static Dictionary<string, int> Pending { get; } = new Dictionary<string, int>();

        internal static HashSet<string> Matched { get; } = new HashSet<string>();

        /// <summary>The active-incidents board (newest first) and the incidents still waiting to be released onto it.</summary>
        internal static List<LiveBoardEntry> Board { get; } = new List<LiveBoardEntry>();

        internal static List<LiveIncidentRecord> Waiting { get; } = new List<LiveIncidentRecord>();

        /// <summary>Saves the running city, if there is one (a scene with no fleet saves nothing).</summary>
        internal static void Capture()
        {
            Clear();

            var feed = Object.FindAnyObjectByType<LiveIncidentFeed>();

            if (feed == null || feed.Fleet == null)
            {
                return;
            }

            foreach (var unit in feed.Fleet.Units)
            {
                var state = unit.Capture();

                if (state != null)
                {
                    Units.Add(state);
                }
            }

            feed.SaveTo(Assigned, Pending, Matched);

            Board.AddRange(feed.Queue.Board);

            Waiting.AddRange(feed.Queue.WaitingRecords);

            var fires = Object.FindAnyObjectByType<LiveIncidentFires>();

            if (fires != null)
            {
                foreach (var fire in fires.Snapshot().Where(fire => !fire.Out))
                {
                    Fires.Add(new FireState { Id = fire.Id, Position = fire.Position, Heat = fire.Heat, Growth = fire.Growth });
                }
            }

            var response = Object.FindAnyObjectByType<FireResponse>();

            Player = response != null ? response.CarryOver() : null;

            HasData = true;
        }

        /// <summary>Forgets what was saved.</summary>
        internal static void Clear()
        {
            HasData = false;

            Units.Clear();

            Fires.Clear();

            Player = null;

            Assigned.Clear();

            Pending.Clear();

            Matched.Clear();

            Board.Clear();

            Waiting.Clear();
        }
    }
}
