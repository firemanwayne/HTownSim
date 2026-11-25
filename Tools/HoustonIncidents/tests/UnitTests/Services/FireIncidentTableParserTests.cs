namespace HoustonIncidents.Harvester.UnitTests.Services;

/// <summary>
/// Tests for <see cref="FireIncidentTableParser"/>.
/// </summary>
public sealed class FireIncidentTableParserTests
{
    [Fact]
    public void Parse_FirePage_ReadsUnitsAndAlarmLevel()
    {
        var parser = new FireIncidentTableParser();

        var result = parser.Parse(LoadFixture("FireGridView2Sample.html"));

        Assert.True(result.IsSuccess);

        Assert.NotEmpty(result.Value);

        Assert.Equal(new FireIncident("WILLOW CHASE BLVD", "BLK TOMBALL PKWY OB", "370E", "11/24/2026 18:12", "AUTOMATIC ALARM", "0", "E096"), result.Value[0]);
    }

    [Fact]
    public void Parse_SeveralUnits_KeepsThemCommaSeparated()
    {
        var parser = new FireIncidentTableParser();

        var result = parser.Parse(LoadFixture("FireGridView2Sample.html"));

        Assert.True(result.IsSuccess);

        Assert.Contains(result.Value, incident => incident.Units == "A067,M074");
    }

    [Fact]
    public void Parse_AllAgenciesPage_FailsOnMissingColumns()
    {
        var parser = new FireIncidentTableParser();

        var result = parser.Parse(LoadFixture("GridView2Sample.html"));

        Assert.False(result.IsSuccess);
    }

    private static string LoadFixture(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    }
}
