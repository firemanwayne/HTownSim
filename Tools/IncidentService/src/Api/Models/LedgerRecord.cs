namespace IncidentService.Api.Models;

/// <summary>
/// The persisted life of one incident: what it is, which units were assigned, and when it was created, assigned, handled and cleared.
/// </summary>
internal sealed class LedgerRecord
{
    /// <summary>Gets or sets the stable incident id.</summary>
    [JsonPropertyName("incidentId")]
    public string IncidentId { get; set; } = string.Empty;

    /// <summary>Gets or sets the page agency code (FD or PD), or empty for a game-made incident.</summary>
    [JsonPropertyName("agency")]
    public string Agency { get; set; } = string.Empty;

    /// <summary>Gets or sets the incident type as shown.</summary>
    [JsonPropertyName("incidentType")]
    public string IncidentType { get; set; } = string.Empty;

    /// <summary>Gets or sets the address.</summary>
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    /// <summary>Gets or sets the cross street.</summary>
    [JsonPropertyName("crossStreet")]
    public string CrossStreet { get; set; } = string.Empty;

    /// <summary>Gets or sets the Key Map grid reference.</summary>
    [JsonPropertyName("keyMap")]
    public string KeyMap { get; set; } = string.Empty;

    /// <summary>Gets or sets the call-opened time as the City shows it.</summary>
    [JsonPropertyName("callTimeOpened")]
    public string CallTimeOpened { get; set; } = string.Empty;

    /// <summary>Gets or sets the alarm level as shown.</summary>
    [JsonPropertyName("alarmLevel")]
    public string AlarmLevel { get; set; } = string.Empty;

    /// <summary>Gets or sets every unit assigned over the incident's life (never shrinks).</summary>
    [JsonPropertyName("units")]
    public List<string> Units { get; set; } = [];

    /// <summary>Gets or sets the latitude, or 0 when unplaced.</summary>
    [JsonPropertyName("lat")]
    public double Latitude { get; set; }

    /// <summary>Gets or sets the longitude, or 0 when unplaced.</summary>
    [JsonPropertyName("lon")]
    public double Longitude { get; set; }

    /// <summary>Gets or sets the current stage.</summary>
    [JsonPropertyName("stage")]
    public IncidentStage Stage { get; set; }

    /// <summary>Gets or sets the ledger revision at which this record last changed.</summary>
    [JsonPropertyName("revision")]
    public long Revision { get; set; }

    /// <summary>Gets or sets when the incident was created.</summary>
    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Gets or sets when the first unit was assigned.</summary>
    [JsonPropertyName("assignedUtc")]
    public DateTimeOffset? AssignedUtc { get; set; }

    /// <summary>Gets or sets when the incident was handled.</summary>
    [JsonPropertyName("handledUtc")]
    public DateTimeOffset? HandledUtc { get; set; }

    /// <summary>Gets or sets when the incident was cleared.</summary>
    [JsonPropertyName("clearedUtc")]
    public DateTimeOffset? ClearedUtc { get; set; }

    /// <summary>Gets or sets how the incident ended or why it moved on (for example "closed by City").</summary>
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = string.Empty;

    /// <summary>Makes an independent copy, so it can be saved while the ledger keeps changing.</summary>
    /// <returns>The copy.</returns>
    public LedgerRecord Clone()
    {
        var copy = (LedgerRecord)MemberwiseClone();

        copy.Units = [.. Units];

        return copy;
    }
}
