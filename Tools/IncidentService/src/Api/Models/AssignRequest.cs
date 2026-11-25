namespace IncidentService.Api.Models;

/// <summary>
/// Units assigned to an incident by the game. The descriptive fields create the incident when the service has not seen it (a
/// game-made call); they are ignored for a known one.
/// </summary>
internal sealed class AssignRequest
{
    /// <summary>Gets or sets the assigned unit codes or names.</summary>
    [JsonPropertyName("units")]
    public List<string> Units { get; set; } = [];

    /// <summary>Gets or sets the incident type, used only when the incident is new to the service.</summary>
    [JsonPropertyName("incidentType")]
    public string IncidentType { get; set; } = string.Empty;

    /// <summary>Gets or sets the address, used only when the incident is new to the service.</summary>
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    /// <summary>Gets or sets the latitude, used only when the incident is new to the service.</summary>
    [JsonPropertyName("lat")]
    public double Latitude { get; set; }

    /// <summary>Gets or sets the longitude, used only when the incident is new to the service.</summary>
    [JsonPropertyName("lon")]
    public double Longitude { get; set; }
}
