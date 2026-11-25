namespace IncidentService.Api.Models;

/// <summary>
/// One harvester poll: incidents that are new or changed, and ids that left the City page.
/// </summary>
internal sealed class FeedRequest
{
    /// <summary>Gets or sets when the poll ran.</summary>
    [JsonPropertyName("generated")]
    public DateTimeOffset Generated { get; set; }

    /// <summary>Gets or sets the new and updated incidents.</summary>
    [JsonPropertyName("incidents")]
    public List<FeedIncident> Incidents { get; set; } = [];

    /// <summary>Gets or sets the ids that closed.</summary>
    [JsonPropertyName("closed")]
    public List<string> Closed { get; set; } = [];
}
