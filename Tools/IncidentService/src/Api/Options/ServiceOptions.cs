namespace IncidentService.Api.Options;

/// <summary>
/// Settings for the incident service, bound from the <c>Service</c> configuration section (override with environment variables
/// such as <c>Service__HandledHoldSeconds</c>).
/// </summary>
internal sealed class ServiceOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Service";

    /// <summary>Gets or sets the ledger file (environment variables are expanded).</summary>
    public string LedgerPath { get; set; } = @"%LOCALAPPDATA%\HTownSim\IncidentService\ledger.json";

    /// <summary>Gets or sets how long a handled incident is held before it is cleared, in seconds.</summary>
    public int HandledHoldSeconds { get; set; } = 120;

    /// <summary>Gets or sets how many cleared records are kept; the oldest are dropped past this.</summary>
    public int MaximumClearedHistory { get; set; } = 500;

    /// <summary>Gets or sets the seconds between housekeeping passes (clearing and saving).</summary>
    public int TickSeconds { get; set; } = 5;
}
