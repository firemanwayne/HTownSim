#nullable enable
using System.Collections.Generic;
using HTownSim.Runtime.Incidents.Managers;
using HTownSim.Runtime.Incidents.Models;
using HTownSim.Runtime.Incidents.Projections;
using HTownSim.Runtime.Incidents.Sources;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Puts a downed patient on the ground at every live EMS incident inside the map once the City has assigned it a unit, so the
    /// scene of another unit's EMS call looks like one. The incident the player was dispatched to is left to FireResponse, which
    /// places its own patient. This never decides when a patient goes: the incident service (the lifecycle: created, assigned,
    /// handled, cleared) does, and the patient is removed when the queue reports the incident cleared. Once a unit is assigned the
    /// City closing the call does not remove it. Only listens to the live feed.
    /// </summary>
    internal sealed class LiveIncidentPatients : MonoBehaviour
    {
        /// <summary>How long a patient stays after the incident closes, in seconds.</summary>
        private const float ClearSeconds = 4f;

        private LiveIncidentFeed _feed = null!;

        private Collider? _ignore;

        /// <summary>Patients by incident id.</summary>
        private readonly Dictionary<string, DownedCitizen> _patients = new Dictionary<string, DownedCitizen>();

        /// <summary>Number of patients placed now, for verification.</summary>
        internal int Count => _patients.Count;

        /// <summary>Follows the feed; <paramref name="ignore"/> is the player's collider, which the bodies must not collide with.</summary>
        internal void Initialize(LiveIncidentFeed feed, Collider? ignore)
        {
            _feed = feed;

            _ignore = ignore;

            feed.Queue.Released += OnChanged;

            feed.Queue.Updated += OnChanged;

            feed.Queue.Cleared += OnCleared;
        }

        /// <summary>Places (or removes) the patient for an incident that was released or got new units.</summary>
        private void OnChanged(LiveBoardEntry entry)
        {
            var record = entry.Record;

            var id = record.IncidentId;

            // The player's own call gets its patient from FireResponse; do not put a second one there.
            if (entry.DispatchedToPlayer)
            {
                Remove(id, 0f);

                return;
            }

            if (_patients.ContainsKey(id) || !entry.InMap || !record.HasPosition || record.Units.Length == 0 || !LiveDispatchRule.TypeFits(record, LiveUnitKind.Ems))
            {
                return;
            }

            var hash = Hash(id);

            var angle = (hash % 360) * Mathf.Deg2Rad;

            var patient = DownedCitizen.Create();

            patient.name = "LivePatient_" + id;

            if (!patient.Show(new PatientRequest(GeoProjection.ToWorld(record.Geo), new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)), hash, _ignore)))
            {
                Destroy(patient.gameObject);

                return;
            }

            _patients[id] = patient;
        }

        /// <summary>A unit has loaded the patient of an incident: they leave the scene with it.</summary>
        internal void Load(string id)
        {
            Remove(id, 1f);
        }

        /// <summary>The incident is cleared: its patient goes.</summary>
        private void OnCleared(string id)
        {
            Remove(id, ClearSeconds);
        }

        /// <summary>Removes an incident's patient after a delay.</summary>
        private void Remove(string id, float delaySeconds)
        {
            if (!_patients.TryGetValue(id, out var patient))
            {
                return;
            }

            _patients.Remove(id);

            if (patient != null)
            {
                patient.Hide(delaySeconds);

                Destroy(patient.gameObject, delaySeconds + 1f);
            }
        }

        /// <summary>A stable non-negative number from an incident id (picks the patient model and which way they lie).</summary>
        private static int Hash(string id)
        {
            var value = 17;

            foreach (var character in id)
            {
                value = unchecked((value * 31) + character);
            }

            return value & 0x7fffffff;
        }

        private void OnDestroy()
        {
            if (_feed != null && _feed.Queue != null)
            {
                _feed.Queue.Released -= OnChanged;

                _feed.Queue.Updated -= OnChanged;

                _feed.Queue.Cleared -= OnCleared;
            }
        }
    }
}
