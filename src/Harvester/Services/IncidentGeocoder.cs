using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace HoustonIncidents.Harvester.Services;

/// <summary>
/// Places incidents with the City of Houston public GIS layers: a numbered address is looked up in the COH Site Addresses
/// layer; a street with a cross street (<c>HILLCROFT AVE</c> / <c>BLK CLAREWOOD DR</c>) is placed at the closest point
/// between the two road centre lines. Results (and street geometry) are cached for the life of the process.
/// </summary>
/// <param name="httpClient">The HTTP client (timeout set at registration).</param>
/// <param name="options">Supplies the layer URLs.</param>
internal sealed class IncidentGeocoder(HttpClient httpClient, IOptions<IncidentOptions> options) : IIncidentGeocoder
{
    /// <summary>The prefix the page puts in front of a cross street that is a block, not a corner.</summary>
    private const string BlockPrefix = "BLK ";

    /// <summary>Two streets whose closest points are further apart than this (degrees, about 400 m) do not meet.</summary>
    private const double MaxCornerGap = 0.004;

    /// <summary>Places already found (or known to fail), by address and cross street.</summary>
    private readonly ConcurrentDictionary<GeocodeRequest, GeoPoint?> _places = new();

    /// <summary>Road centre-line vertices by street name.</summary>
    private readonly ConcurrentDictionary<string, IReadOnlyList<GeoPoint>> _streets = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<GeoPoint?> LocateAsync(GeocodeRequest request, CancellationToken cancellationToken)
    {
        if (_places.TryGetValue(request, out var known))
        {
            return known;
        }

        var place = char.IsDigit(request.Address.FirstOrDefault())
            ? await SiteAddressAsync(request.Address, cancellationToken).ConfigureAwait(false)
            : null;

        if (place is null && request.CrossStreet.Length > 0)
        {
            place = await CornerAsync(request, cancellationToken).ConfigureAwait(false);
        }

        _places[request] = place;

        return place;
    }

    /// <summary>The point of a numbered address in the Site Addresses layer, or null.</summary>
    private async Task<GeoPoint?> SiteAddressAsync(string address, CancellationToken cancellationToken)
    {
        var where = "fulladdr='" + address.Replace("'", "''", StringComparison.Ordinal) + "'";

        var features = await QueryAsync(new LayerQuery(options.Value.SiteAddressUrl, where, 1), cancellationToken).ConfigureAwait(false);

        foreach (var feature in features)
        {
            if (feature.TryGetProperty("geometry", out var geometry) && geometry.TryGetProperty("x", out var x) && geometry.TryGetProperty("y", out var y))
            {
                return new GeoPoint(y.GetDouble(), x.GetDouble());
            }
        }

        return null;
    }

    /// <summary>The meeting point of the address street and the cross street, or null when they do not meet.</summary>
    private async Task<GeoPoint?> CornerAsync(GeocodeRequest request, CancellationToken cancellationToken)
    {
        var cross = request.CrossStreet.StartsWith(BlockPrefix, StringComparison.OrdinalIgnoreCase)
            ? request.CrossStreet.Substring(BlockPrefix.Length)
            : request.CrossStreet;

        var street = StreetName(request.Address);

        var first = await StreetAsync(street, cancellationToken).ConfigureAwait(false);

        var second = await StreetAsync(cross.Trim(), cancellationToken).ConfigureAwait(false);

        if (first.Count == 0 || second.Count == 0)
        {
            return null;
        }

        var best = double.MaxValue;

        GeoPoint? corner = null;

        foreach (var a in first)
        {
            foreach (var b in second)
            {
                var gap = Math.Abs(a.Latitude - b.Latitude) + Math.Abs(a.Longitude - b.Longitude);

                if (gap < best)
                {
                    best = gap;

                    corner = new GeoPoint((a.Latitude + b.Latitude) / 2, (a.Longitude + b.Longitude) / 2);
                }
            }
        }

        return best <= MaxCornerGap ? corner : null;
    }

    /// <summary>The street name of an address (without a leading house number).</summary>
    private static string StreetName(string address)
    {
        var parts = address.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 2 && char.IsDigit(parts[0][0]) ? parts[1] : address;
    }

    /// <summary>All centre-line vertices of a named street (cached).</summary>
    private async Task<IReadOnlyList<GeoPoint>> StreetAsync(string name, CancellationToken cancellationToken)
    {
        if (_streets.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var where = "fullname='" + name.Replace("'", "''", StringComparison.Ordinal) + "'";

        var features = await QueryAsync(new LayerQuery(options.Value.RoadCenterlineUrl, where, 2000), cancellationToken).ConfigureAwait(false);

        var points = new List<GeoPoint>();

        foreach (var feature in features)
        {
            if (!feature.TryGetProperty("geometry", out var geometry) || !geometry.TryGetProperty("paths", out var paths))
            {
                continue;
            }

            foreach (var path in paths.EnumerateArray())
            {
                foreach (var vertex in path.EnumerateArray())
                {
                    points.Add(new GeoPoint(vertex[1].GetDouble(), vertex[0].GetDouble()));
                }
            }
        }

        _streets[name] = points;

        return points;
    }

    /// <summary>Runs an ArcGIS feature query (WGS84 geometry) and returns its features; empty on any failure.</summary>
    private async Task<IReadOnlyList<JsonElement>> QueryAsync(LayerQuery query, CancellationToken cancellationToken)
    {
        var url = query.LayerUrl + "/query?where=" + Uri.EscapeDataString(query.Where)
            + "&outFields=OBJECTID&returnGeometry=true&outSR=4326&f=json&resultRecordCount="
            + query.MaxRecords.ToString(CultureInfo.InvariantCulture);

        try
        {
            using var stream = await httpClient
                .GetStreamAsync(url, cancellationToken)
                .ConfigureAwait(false);

            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("features", out var features))
            {
                return [];
            }

            return features
                .EnumerateArray()
                .Select(feature => feature.Clone())
                .ToList();
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }
}
