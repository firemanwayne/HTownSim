#nullable enable
using HTownSim.Runtime.Contracts.Models;

namespace HTownSim.Runtime.Contracts.Interfaces
{
    /// <summary>Traffic collision behavior available to a vehicle controller.</summary>
    internal interface ITrafficInteractions
    {
        /// <summary>Applies an impact and reports whether a traffic participant was hit.</summary>
        bool TryImpact(TrafficImpact impact);
    }
}
