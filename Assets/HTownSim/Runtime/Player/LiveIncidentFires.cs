#nullable enable
using System.Collections.Generic;
using System.Linq;
using HTownSim.Runtime.Incidents.Sources;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The flames, smoke and crackle at a live fire that the computer-driven units are sent to. The fire starts when they are sent and grows
    /// while nobody is working it; once units are on the scene the heat falls (the more units, the faster) until the fire is out, and the
    /// scene is cleared a little after the incident is. The player's own fire is made by <see cref="FireResponse"/> and is not touched here.
    /// </summary>
    internal sealed class LiveIncidentFires : MonoBehaviour
    {
        /// <summary>The most computer-handled fires burning at once (each is a particle system with smoke). Raise it to show more fires (costs frame rate); lower it to save frame rate.</summary>
        private const int MaximumFires = 4;

        /// <summary>Seconds for a fire nobody is working to reach full growth. Raise it for a fire that grows slowly; lower it for one that blows up quickly.</summary>
        private const float GrowthSeconds = 75f;

        /// <summary>Seconds for one unit on scene to put out a full fire (more units on scene work faster). Raise it for longer fires; lower it for quick knock-downs.</summary>
        private const float KnockdownSeconds = 90f;

        /// <summary>How long (seconds) a fire that is out stays (its smoke thins) before it is removed. Raise it to leave smoke and embers longer; lower it to clear the scene sooner.</summary>
        private const float LingerSeconds = 25f;

        /// <summary>How long (seconds) a fire stays after its incident is cleared. Raise it to keep the scene visible longer; lower it to remove it at once.</summary>
        private const float ClearedLingerSeconds = 6f;

        /// <summary>The ground probe for the fire's height: from this far above, this far down (metres).</summary>
        private const float ProbeUp = 60f;

        private const float ProbeDown = 120f;

        private readonly Dictionary<string, Burning> _fires = new Dictionary<string, Burning>();

        private LiveIncidentFeed _feed = null!;

        private GameObject _prefab = null!;

        private AiFleet? _fleet;

        /// <summary>How many fires are burning or lingering now, for verification.</summary>
        internal int Count => _fires.Count;

        /// <summary>Follows the feed; <paramref name="prefab"/> is the authored fire.</summary>
        internal void Initialize(LiveIncidentFeed feed, GameObject prefab)
        {
            _feed = feed;

            _fleet = feed.Fleet;

            _prefab = prefab;

            feed.FireDispatched += OnFireDispatched;

            feed.Queue.Cleared += OnCleared;

            RestoreCarryOver();
        }

        /// <summary>Lights the fires that were burning when the player switched unit (see <see cref="CityCarryOver"/>), as hot and as grown as they were.</summary>
        private void RestoreCarryOver()
        {
            if (!CityCarryOver.HasData)
            {
                return;
            }

            foreach (var saved in CityCarryOver.Fires)
            {
                OnFireDispatched(saved.Id, saved.Position);

                if (_fires.TryGetValue(saved.Id, out var fire))
                {
                    fire.Heat = saved.Heat;

                    fire.Growth = saved.Growth;
                }
            }
        }

        private void OnFireDispatched(string incidentId, Vector3 world)
        {
            if (_fires.ContainsKey(incidentId) || _fires.Count >= MaximumFires)
            {
                return;
            }

            if (Physics.Raycast(new Vector3(world.x, ProbeUp, world.z), Vector3.down, out var hit, ProbeUp + ProbeDown, ~0, QueryTriggerInteraction.Ignore))
            {
                world.y = hit.point.y;
            }

            _fires[incidentId] = new Burning(new LiveFireVfx(_prefab, world, transform.parent), world);
        }

        /// <summary>The fires burning now (id, where, heat and growth, and whether it is out and only smoking), for a spectator to show.</summary>
        internal IEnumerable<(string Id, Vector3 Position, float Heat, float Growth, bool Out)> Snapshot()
        {
            foreach (var pair in _fires)
            {
                yield return (pair.Key, pair.Value.Position, pair.Value.Heat, pair.Value.Growth, pair.Value.RemoveIn >= 0f);
            }
        }

        private void OnCleared(string incidentId)
        {
            if (_fires.TryGetValue(incidentId, out var fire))
            {
                fire.Vfx.Stop();

                fire.RemoveIn = Mathf.Min(fire.RemoveIn < 0f ? ClearedLingerSeconds : fire.RemoveIn, ClearedLingerSeconds);
            }
        }

        private void Update()
        {
            var dt = Time.deltaTime;

            foreach (var id in _fires.Keys.ToList())
            {
                var fire = _fires[id];

                if (fire.RemoveIn >= 0f)
                {
                    fire.RemoveIn -= dt;

                    if (fire.RemoveIn <= 0f)
                    {
                        fire.Vfx.Destroy();

                        _fires.Remove(id);
                    }

                    continue;
                }

                var attended = _fleet == null ? 0 : _fleet.UnitsOnScene(id);

                if (attended == 0)
                {
                    fire.Growth = Mathf.Min(1f, fire.Growth + (dt / GrowthSeconds));
                }
                else
                {
                    fire.Heat = Mathf.Max(0f, fire.Heat - (attended * dt / KnockdownSeconds));
                }

                fire.Vfx.Set(fire.Heat, fire.Growth);

                if (fire.Heat <= 0f)
                {
                    fire.Vfx.Stop();

                    fire.RemoveIn = LingerSeconds;
                }
            }
        }

        private void OnDestroy()
        {
            if (_feed != null)
            {
                _feed.FireDispatched -= OnFireDispatched;

                if (_feed.Queue != null)
                {
                    _feed.Queue.Cleared -= OnCleared;
                }
            }

            foreach (var fire in _fires.Values)
            {
                fire.Vfx.Destroy();
            }
        }

        /// <summary>A fire in progress: its effect, how grown and how hot it is, and the time left before it is removed (negative while it burns).</summary>
        private sealed class Burning
        {
            internal Burning(LiveFireVfx vfx, Vector3 position)
            {
                Vfx = vfx;

                Position = position;
            }

            internal LiveFireVfx Vfx { get; }

            internal Vector3 Position { get; }

            internal float Growth { get; set; }

            internal float Heat { get; set; } = 1f;

            internal float RemoveIn { get; set; } = -1f;
        }
    }
}
