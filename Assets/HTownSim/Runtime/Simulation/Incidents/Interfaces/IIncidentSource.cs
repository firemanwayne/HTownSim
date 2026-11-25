#nullable enable
using HTownSim.Runtime.Incidents.Managers;

namespace HTownSim.Runtime.Incidents.Interfaces
{
    /// <summary>Something that feeds incident JSON to the <see cref="IncidentManager"/>: a watched folder now, a live feed later.</summary>
    internal interface IIncidentSource
    {
        /// <summary>A short name for the log.</summary>
        string Name { get; }

        /// <summary>Called every frame; submits whatever new incidents the source has through <c>manager.Submit</c>.</summary>
        void Poll(IncidentManager manager);
    }
}
