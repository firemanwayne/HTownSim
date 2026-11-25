#nullable enable
using System;
using System.Collections;
using System.Text;
using HTownSim.Runtime.Incidents.Models;
using UnityEngine;
using UnityEngine.Networking;

namespace HTownSim.Runtime.Incidents.Clients
{
    /// <summary>
    /// Talks to the incident service (Tools/IncidentService), which owns the incident lifecycle: created, assigned, handled, cleared.
    /// It polls for what changed since the last revision and hands each answer to <see cref="Received"/> in the live-feed shape, and it
    /// reports back which units the game assigned and how a call ended. The service being down is normal (the game then runs on feed
    /// files alone), so a failed request is only logged, and only once per outage.
    /// </summary>
    internal sealed class IncidentServiceClient : MonoBehaviour
    {
        /// <summary>Environment variable that overrides the service address.</summary>
        internal const string AddressVariable = "HTOWNSIM_INCIDENT_SERVICE";

        /// <summary>The service address when the variable is not set.</summary>
        private const string DefaultAddress = "http://127.0.0.1:5870";

        /// <summary>Seconds between polls.</summary>
        private const float PollSeconds = 2f;

        /// <summary>Seconds to wait for the service to answer.</summary>
        private const int TimeoutSeconds = 5;

        private string _address = DefaultAddress;

        private long _revision;

        private bool _reachable = true;

        /// <summary>Raised with each batch of changes from the service.</summary>
        internal event Action<LiveFeedFile>? Received;

        /// <summary>True while the last request to the service worked.</summary>
        internal bool Connected { get; private set; }

        /// <summary>Starts polling the service.</summary>
        internal void Begin()
        {
            var configured = Environment.GetEnvironmentVariable(AddressVariable);

            _address = string.IsNullOrWhiteSpace(configured) ? DefaultAddress : configured.TrimEnd('/');

            StartCoroutine(PollLoop());
        }

        /// <summary>Tells the service units were assigned to an incident.</summary>
        internal void Assign(string incidentId, AssignPayload payload)
        {
            StartCoroutine(Post("/api/incidents/" + UnityWebRequest.EscapeURL(incidentId) + "/assign", JsonUtility.ToJson(payload)));
        }

        /// <summary>Tells the service an incident is finished (cleared) and how it ended.</summary>
        internal void Clear(string incidentId, string outcome)
        {
            StartCoroutine(Post("/api/incidents/" + UnityWebRequest.EscapeURL(incidentId) + "/clear", JsonUtility.ToJson(new OutcomePayload(outcome))));
        }

        private IEnumerator PollLoop()
        {
            var wait = new WaitForSecondsRealtime(PollSeconds);

            while (true)
            {
                using (var request = UnityWebRequest.Get(_address + "/api/incidents?since=" + _revision))
                {
                    request.timeout = TimeoutSeconds;

                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Accept(request.downloadHandler.text);
                    }
                    else
                    {
                        NoteFailure(request.error);
                    }
                }

                yield return wait;
            }
        }

        /// <summary>Reads one answer and passes it on.</summary>
        private void Accept(string json)
        {
            LiveFeedFile? feed;

            try
            {
                feed = JsonUtility.FromJson<LiveFeedFile>(json);
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning("Incident service: could not read the answer: " + exception.Message);

                return;
            }

            if (feed == null)
            {
                return;
            }

            if (!Connected)
            {
                Debug.Log("Incident service connected at " + _address);
            }

            Connected = true;

            _reachable = true;

            _revision = feed.Revision;

            Received?.Invoke(feed);
        }

        private IEnumerator Post(string path, string json)
        {
            using var request = new UnityWebRequest(_address + path, UnityWebRequest.kHttpVerbPOST);

            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));

            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");

            request.timeout = TimeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                NoteFailure(request.error);
            }
        }

        /// <summary>Logs a failed request once per outage.</summary>
        private void NoteFailure(string error)
        {
            Connected = false;

            if (_reachable)
            {
                Debug.LogWarning("Incident service not reachable at " + _address + " (" + error + "); running on feed files only.");

                _reachable = false;
            }
        }
    }
}
