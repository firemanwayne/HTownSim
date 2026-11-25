namespace IncidentService.Api.Models;

/// <summary>
/// The ledger as saved to disk.
/// </summary>
internal sealed class LedgerFile
{
    /// <summary>Gets or sets the saved records.</summary>
    [JsonPropertyName("records")]
    public List<LedgerRecord> Records { get; set; } = [];
}
