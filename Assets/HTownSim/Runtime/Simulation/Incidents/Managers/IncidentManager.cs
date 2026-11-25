#nullable enable
using System;
using System.Collections.Generic;
using HTownSim.Runtime.Incidents.Interfaces;
using HTownSim.Runtime.Incidents.Models;
using HTownSim.Runtime.Incidents.Parsers;
using HTownSim.Runtime.Incidents.Sources;
using UnityEngine;

namespace HTownSim.Runtime.Incidents.Managers
{
    /// <summary>
    /// The one door every emergency comes through. Sources (the inbox folder, a future scanner feed, anything calling
    /// <see cref="Submit(string, string)"/>) hand it standard incident JSON; it validates, de-duplicates and queues the calls,
    /// and the game takes them with <see cref="TakeNext"/>. The random fallback calls are made as the same JSON and go through
    /// the same path, so a call from the city's data and a made-up call start an emergency exactly the same way.
    /// </summary>
    internal sealed class IncidentManager : MonoBehaviour
    {
        /// <summary>The most calls waiting at once; further ones are refused until the player catches up.</summary>
        private const int MaxQueued = 50;

        /// <summary>When this many ids have been seen the memory is cleared, so a long session cannot grow without bound.</summary>
        private const int MaxSeenIds = 5000;

        private readonly List<Incident> _queue = new List<Incident>();

        private readonly HashSet<string> _seen = new HashSet<string>();

        private readonly List<IIncidentSource> _sources = new List<IIncidentSource>();

        private readonly FallbackIncidentSource _fallback = new FallbackIncidentSource();

        private IncidentManagerSettings _settings;

        /// <summary>The manager of the running scene, for code that is not handed a reference; null before the scene has one.</summary>
        internal static IncidentManager? Instance { get; private set; }

        /// <summary>Raised when a call joins the queue.</summary>
        internal event Action<Incident>? Accepted;

        /// <summary>Raised with the reason when a payload is refused.</summary>
        internal event Action<string>? Rejected;

        /// <summary>Raised when the game reports a call finished.</summary>
        internal event Action<Incident, IncidentOutcome>? Completed;

        /// <summary>True when at least one call is waiting to be dispatched.</summary>
        internal bool HasPending => _queue.Count > 0;

        /// <summary>The folder watched for incident JSON files.</summary>
        internal string InboxDirectory => _settings.InboxDirectory;

        /// <summary>Sets the playable area and starts watching the inbox folder.</summary>
        internal void Initialize(IncidentManagerSettings settings)
        {
            _settings = settings;

            _sources.Clear();

            _sources.Add(new FolderIncidentSource(settings.InboxDirectory));

            Debug.Log("IncidentManager ready. Drop incident JSON files in " + settings.InboxDirectory);
        }

        /// <summary>Validates incident JSON (one object, a batch, or JSON Lines) and queues the good calls.</summary>
        internal SubmitResult Submit(string json, string source)
        {
            var result = new SubmitResult();

            foreach (var parsed in IncidentParser.ParseJson(json, source))
            {
                Enqueue(parsed, result);
            }

            return result;
        }

        /// <summary>Makes a random call as standard JSON and submits it like any other source's; null when no site is eligible.</summary>
        internal SubmitResult? SubmitFallback(FallbackRequest request)
        {
            var payload = _fallback.Create(request);

            return payload is null ? null : Submit(JsonUtility.ToJson(payload), FallbackIncidentSource.SourceName);
        }

        /// <summary>Takes the most severe waiting call (the oldest of equals); null when none waits.</summary>
        internal Incident? TakeNext()
        {
            if (_queue.Count == 0)
            {
                return null;
            }

            var best = 0;

            for (var i = 1; i < _queue.Count; i++)
            {
                if (_queue[i].Severity > _queue[best].Severity)
                {
                    best = i;
                }
            }

            var incident = _queue[best];

            _queue.RemoveAt(best);

            return incident;
        }

        /// <summary>Tells listeners a call has ended.</summary>
        internal void Complete(Incident incident, IncidentOutcome outcome)
        {
            Debug.Log("Incident " + incident.Id + " " + outcome.ToString().ToLowerInvariant() + ".");

            Completed?.Invoke(incident, outcome);
        }

        /// <summary>Registers this manager as the running scene's.</summary>
        private void OnEnable()
        {
            Instance = this;
        }

        /// <summary>Unregisters this manager.</summary>
        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Lets every source look for new calls.</summary>
        private void Update()
        {
            foreach (var source in _sources)
            {
                source.Poll(this);
            }
        }

        /// <summary>Applies the manager's own rules to a parsed payload: not seen before, inside the playable area, room in the queue.</summary>
        private void Enqueue(IncidentParseResult parsed, SubmitResult result)
        {
            if (!parsed.Ok)
            {
                Reject(parsed.Id, parsed.Error, result);

                return;
            }

            var incident = parsed.Incident!;

            if (_seen.Contains(incident.Id))
            {
                Reject(incident.Id, "incidentId was already received this session", result);

                return;
            }

            if (_settings.PlayableArea is { } area && !area.Contains(new Vector2(incident.WorldPosition.x, incident.WorldPosition.z)))
            {
                Reject(incident.Id, "position is outside the dispatch area (scene x " + incident.WorldPosition.x.ToString("0") + ", z " + incident.WorldPosition.z.ToString("0") + ")", result);

                return;
            }

            if (_queue.Count >= MaxQueued)
            {
                Reject(incident.Id, "the dispatch queue is full", result);

                return;
            }

            if (_seen.Count >= MaxSeenIds)
            {
                _seen.Clear();
            }

            _seen.Add(incident.Id);

            _queue.Add(incident);

            result.AddAccepted();

            Debug.Log("Incident " + incident.Id + " queued: " + incident.Agency + " " + incident.CallLabel + ", severity " + incident.Severity + ", needs " + incident.RequiredUnitsText() + " (" + incident.Source + ").");

            Accepted?.Invoke(incident);
        }

        /// <summary>Records and announces a refused payload.</summary>
        private void Reject(string id, string reason, SubmitResult result)
        {
            var message = (id.Length > 0 ? "Incident " + id : "Incident payload") + " refused: " + reason;

            result.AddRejected(message);

            Debug.LogWarning(message);

            Rejected?.Invoke(message);
        }
    }
}
