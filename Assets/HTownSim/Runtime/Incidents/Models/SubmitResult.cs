#nullable enable
using System.Collections.Generic;

namespace HTownSim.Runtime.Incidents.Models
{
    /// <summary>What happened to the payload(s) in one <c>IncidentManager.Submit</c> call.</summary>
    internal sealed class SubmitResult
    {
        private readonly List<string> _errors = new List<string>();

        /// <summary>How many incidents joined the queue.</summary>
        internal int Accepted { get; private set; }

        /// <summary>How many payloads were refused, including repeats of an id already seen.</summary>
        internal int Rejected => _errors.Count;

        /// <summary>One line per refused payload.</summary>
        internal IReadOnlyList<string> Errors => _errors;

        /// <summary>Counts an accepted incident.</summary>
        internal void AddAccepted()
        {
            Accepted++;
        }

        /// <summary>Records a refused payload.</summary>
        internal void AddRejected(string error)
        {
            _errors.Add(error);
        }
    }
}
