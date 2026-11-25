using System.Text.Json.Serialization;

namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// One feed file for the game: incidents that are new or changed (units added) since the last file, and ids that closed.
/// </summary>
internal sealed class LiveFeed
{
    /// <summary>Gets or sets when the poll ran.</summary>
    [JsonPropertyName("generated")]
    public DateTimeOffset Generated { get; set; }

    /// <summary>Gets or sets the new and updated incidents.</summary>
    [JsonPropertyName("incidents")]
    public IReadOnlyList<LiveIncident> Incidents { get; set; } = [];

    /// <summary>Gets or sets the ids of incidents that left the page.</summary>
    [JsonPropertyName("closed")]
    public IReadOnlyList<string> Closed { get; set; } = [];
}
