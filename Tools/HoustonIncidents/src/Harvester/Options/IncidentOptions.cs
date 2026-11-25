namespace HoustonIncidents.Harvester.Options;

/// <summary>
/// Settings for the incident harvester, bound from the <c>Incidents</c> configuration section
/// (override with environment variables such as <c>Incidents__PollIntervalSeconds</c>).
/// </summary>
internal sealed class IncidentOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Incidents";

    /// <summary>Gets or sets the page URL.</summary>
    public string Url { get; set; } = "https://cohweb.houstontx.gov/ActiveIncidents/Combined.aspx?agency=%25";

    /// <summary>Gets or sets the seconds between polls.</summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>Gets or sets the per-request timeout in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 20;

    /// <summary>Gets or sets the User-Agent header sent with each request.</summary>
    public string UserAgent { get; set; } = "Mozilla/5.0 (compatible; HoustonIncidentsHarvester/1.0)";

    /// <summary>Gets or sets the fire-only page URL, which adds the assigned units and the alarm level.</summary>
    public string FireUrl { get; set; } = "https://cohweb.houstontx.gov/ActiveIncidents/Combined.aspx?agency=F";

    /// <summary>Gets or sets the COH Site Addresses layer (numbered addresses to points).</summary>
    public string SiteAddressUrl { get; set; } = "https://services.arcgis.com/NummVBqZSIJKUeVR/arcgis/rest/services/COH_SiteAddresses/FeatureServer/0";

    /// <summary>Gets or sets the COH road centre-line layer (street and cross street to a corner).</summary>
    public string RoadCenterlineUrl { get; set; } = "https://services.arcgis.com/NummVBqZSIJKUeVR/arcgis/rest/services/COH_RoadCenterline/FeatureServer/0";

    /// <summary>
    /// Gets or sets the HTownSim live-incident inbox the feed files are written to (environment variables are expanded).
    /// Empty turns the game feed off.
    /// </summary>
    public string GameInboxDirectory { get; set; } = @"%USERPROFILE%\AppData\LocalLow\DefaultCompany\HTownSim\Incidents\Live";
}
