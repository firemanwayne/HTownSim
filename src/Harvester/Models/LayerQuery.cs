namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// An ArcGIS feature-layer query: the layer, the SQL filter and how many features to return at most.
/// </summary>
/// <param name="LayerUrl">The layer URL, ending in <c>/FeatureServer/&lt;n&gt;</c>.</param>
/// <param name="Where">The SQL where clause.</param>
/// <param name="MaxRecords">The most features to return.</param>
internal sealed record LayerQuery(string LayerUrl, string Where, int MaxRecords);
