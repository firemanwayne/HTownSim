namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// What to place: an address (with a house number, or a street name alone) and an optional cross street.
/// </summary>
/// <param name="Address">The address, e.g. <c>6510 HILLCROFT AVE</c> or <c>HILLCROFT AVE</c>.</param>
/// <param name="CrossStreet">The cross street, e.g. <c>BLK CLAREWOOD DR</c>, or empty.</param>
internal sealed record GeocodeRequest(string Address, string CrossStreet);
