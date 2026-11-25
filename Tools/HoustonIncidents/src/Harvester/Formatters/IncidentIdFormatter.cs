using System.Security.Cryptography;
using System.Text;

namespace HoustonIncidents.Harvester.Formatters;

/// <summary>
/// Builds a stable id for an incident from the fields that identify it (the page publishes no id).
/// </summary>
internal static class IncidentIdFormatter
{
    /// <summary>Prefix of every id.</summary>
    private const string Prefix = "COH-";

    /// <summary>Number of hex characters kept from the hash.</summary>
    private const int HashLength = 12;

    /// <summary>
    /// Formats the id of an incident: <c>COH-</c> and the first 12 hex digits of the SHA-1 of its identifying fields.
    /// </summary>
    /// <param name="incident">The incident.</param>
    /// <returns>The id, the same on every poll.</returns>
    public static string Format(Incident incident)
    {
        var key = string.Join('|', incident.Agency, incident.Address, incident.IncidentType, incident.CallTimeOpened);

        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));

        return Prefix + Convert
            .ToHexString(hash)
            .Substring(0, HashLength);
    }
}
