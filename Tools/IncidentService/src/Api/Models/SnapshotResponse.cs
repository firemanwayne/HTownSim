namespace IncidentService.Api.Models;

/// <summary>
/// What changed in the ledger since a revision, in the game's live-feed shape (so the game reads it like a feed file) plus the
/// ids that were cleared.
/// </summary>
internal sealed class SnapshotResponse
{
    /// <summary>Gets or sets the ledger revision this snapshot is current to; ask for changes since it next time.</summary>
    [JsonPropertyName("revision")]
    public long Revision { get; set; }

    /// <summary>Gets or sets the created and assigned incidents that changed.</summary>
    [JsonPropertyName("incidents")]
    public List<FeedIncident> Incidents { get; set; } = [];

    /// <summary>Gets or sets the ids that were handled or cleared (off the active board).</summary>
    [JsonPropertyName("closed")]
    public List<string> Closed { get; set; } = [];

    /// <summary>Gets or sets the ids that were cleared (their scene, such as a patient, can go).</summary>
    [JsonPropertyName("cleared")]
    public List<string> Cleared { get; set; } = [];
}
