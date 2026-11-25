namespace HoustonIncidents.Harvester.UnitTests.Formatters;

/// <summary>
/// Tests for <see cref="IncidentChangeFormatter"/>.
/// </summary>
public sealed class IncidentChangeFormatterTests
{
    private static readonly Incident Sample = new("FD", "AUTUMN FOREST DR", "EMS EVENT", "11/24/2026 09:33");

    private static readonly DateTimeOffset Timestamp = new(2026, 11, 24, 9, 33, 12, TimeSpan.Zero);

    [Fact]
    public void Format_NewChange_MatchesSpecLine()
    {
        var line = IncidentChangeFormatter.Format(new IncidentChange(IncidentChangeKind.New, Sample), Timestamp);

        Assert.Equal("[09:33:12] NEW       FD | AUTUMN FOREST DR | EMS EVENT | 11/24/2026 09:33", line);
    }

    [Fact]
    public void Format_ClosedChange_MatchesSpecLine()
    {
        var line = IncidentChangeFormatter.Format(new IncidentChange(IncidentChangeKind.Closed, Sample), Timestamp);

        Assert.Equal("[09:33:12] CLOSED    FD | AUTUMN FOREST DR | EMS EVENT | 11/24/2026 09:33", line);
    }
}
