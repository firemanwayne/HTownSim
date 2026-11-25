namespace IncidentService.Api.Models;

/// <summary>
/// How an incident ended, sent when the game reports it handled or cleared.
/// </summary>
internal sealed class OutcomeRequest
{
    /// <summary>Gets or sets the outcome text (for example cleared or failed).</summary>
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = string.Empty;
}
