namespace HoustonIncidents.Harvester.UnitTests.Formatters;

/// <summary>
/// Tests for <see cref="IncidentIdFormatter"/>.
/// </summary>
public sealed class IncidentIdFormatterTests
{
    [Fact]
    public void Format_SameIncident_GivesSameId()
    {
        var first = IncidentIdFormatter.Format(new Incident("FD", "HILLCROFT AVE", "EMS EVENT", "11/24/2026 18:11"));

        var second = IncidentIdFormatter.Format(new Incident("FD", "HILLCROFT AVE", "EMS EVENT", "11/24/2026 18:11"));

        Assert.Equal(first, second);

        Assert.StartsWith("COH-", first);

        Assert.Equal(16, first.Length);
    }

    [Fact]
    public void Format_DifferentCallTime_GivesDifferentId()
    {
        var first = IncidentIdFormatter.Format(new Incident("FD", "HILLCROFT AVE", "EMS EVENT", "11/24/2026 18:11"));

        var second = IncidentIdFormatter.Format(new Incident("FD", "HILLCROFT AVE", "EMS EVENT", "11/24/2026 18:12"));

        Assert.NotEqual(first, second);
    }
}
