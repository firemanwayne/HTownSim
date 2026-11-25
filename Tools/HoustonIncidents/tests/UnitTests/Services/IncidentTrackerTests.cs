namespace HoustonIncidents.Harvester.UnitTests.Services;

/// <summary>
/// Tests for <see cref="IncidentTracker"/>.
/// </summary>
public sealed class IncidentTrackerTests
{
    private static readonly Incident First = new("FD", "AUTUMN FOREST DR", "EMS EVENT", "11/24/2026 09:33");

    private static readonly Incident Second = new("PD", "MAIN ST", "DISTURBANCE", "11/24/2026 09:35");

    [Fact]
    public void Apply_FirstSnapshot_ReportsAllAsNew()
    {
        var tracker = new IncidentTracker();

        var changes = tracker.Apply([First, Second]);

        Assert.Equal(
            [new IncidentChange(IncidentChangeKind.New, First), new IncidentChange(IncidentChangeKind.New, Second)],
            changes);
    }

    [Fact]
    public void Apply_SameSnapshotTwice_ReportsNothingSecondTime()
    {
        var tracker = new IncidentTracker();

        tracker.Apply([First, Second]);

        var changes = tracker.Apply([First, Second]);

        Assert.Empty(changes);
    }

    [Fact]
    public void Apply_IncidentDisappears_ReportsClosed()
    {
        var tracker = new IncidentTracker();

        tracker.Apply([First, Second]);

        var changes = tracker.Apply([Second]);

        Assert.Equal([new IncidentChange(IncidentChangeKind.Closed, First)], changes);
    }

    [Fact]
    public void Apply_NewIncidentAppears_ReportsOnlyTheNewOne()
    {
        var tracker = new IncidentTracker();

        tracker.Apply([First]);

        var changes = tracker.Apply([First, Second]);

        Assert.Equal([new IncidentChange(IncidentChangeKind.New, Second)], changes);
    }

    [Fact]
    public void Apply_DuplicateRowsInSnapshot_ReportsNewOnce()
    {
        var tracker = new IncidentTracker();

        var changes = tracker.Apply([First, First]);

        Assert.Single(changes);
    }

    [Fact]
    public void Apply_ClosedIncidentReturns_ReportsNewAgain()
    {
        var tracker = new IncidentTracker();

        tracker.Apply([First]);

        tracker.Apply([]);

        var changes = tracker.Apply([First]);

        Assert.Equal([new IncidentChange(IncidentChangeKind.New, First)], changes);
    }

    [Fact]
    public void Apply_EmptySnapshotAfterIncidents_ClosesAll()
    {
        var tracker = new IncidentTracker();

        tracker.Apply([First, Second]);

        var changes = tracker.Apply([]);

        Assert.Equal(2, changes.Count(change => change.Kind == IncidentChangeKind.Closed));
    }
}
