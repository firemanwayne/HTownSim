namespace HoustonIncidents.Harvester.UnitTests.Services;

/// <summary>
/// Tests for <see cref="IncidentTableParser"/>.
/// </summary>
public sealed class IncidentTableParserTests
{
    [Fact]
    public void Parse_GridViewPresent_ReturnsFourFieldIncidents()
    {
        var parser = new IncidentTableParser();

        var result = parser.Parse(LoadFixture("GridView2Sample.html"));

        Assert.True(result.IsSuccess);

        Assert.Equal(2, result.Value.Count);

        Assert.Equal(new Incident("FD", "AUTUMN FOREST DR", "EMS EVENT", "11/24/2026 09:33"), result.Value[0]);
    }

    [Fact]
    public void Parse_EntitiesAndWhitespace_AreDecodedAndCollapsed()
    {
        var parser = new IncidentTableParser();

        var result = parser.Parse(LoadFixture("GridView2Sample.html"));

        Assert.True(result.IsSuccess);

        Assert.Equal("MAIN & ELM ST", result.Value[1].Address);
    }

    [Fact]
    public void Parse_OtherTablesPresent_OnlyGridViewIsRead()
    {
        var parser = new IncidentTableParser();

        var result = parser.Parse(LoadFixture("GridView2Sample.html"));

        Assert.True(result.IsSuccess);

        Assert.DoesNotContain(result.Value, incident => incident.Agency == "ignore me");
    }

    [Fact]
    public void Parse_NoGridView_ReturnsFailure()
    {
        var parser = new IncidentTableParser();

        var result = parser.Parse("<html><body><p>Service unavailable</p></body></html>");

        Assert.False(result.IsSuccess);

        Assert.Contains("GridView2", result.Error);
    }

    [Fact]
    public void Parse_MissingRequiredColumn_ReturnsFailure()
    {
        var parser = new IncidentTableParser();

        var html = "<table id=\"GridView2\"><tr><th>Agency</th><th>Address</th></tr></table>";

        var result = parser.Parse(html);

        Assert.False(result.IsSuccess);

        Assert.Contains("Incident Type", result.Error);
    }

    [Fact]
    public void Parse_HeaderOnlyTable_ReturnsEmptySuccess()
    {
        var parser = new IncidentTableParser();

        var html = "<table id=\"GridView2\"><tr><th>Agency</th><th>Address</th><th>Incident Type</th><th>Call Time(Opened)</th></tr></table>";

        var result = parser.Parse(html);

        Assert.True(result.IsSuccess);

        Assert.Empty(result.Value);
    }

    private static string LoadFixture(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    }
}
