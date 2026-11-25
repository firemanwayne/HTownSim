namespace IncidentService.Api.UnitTests.Services;

public sealed class IncidentLedgerTests
{
    [Fact]
    public void Ingest_NewIncidentWithoutUnits_IsCreated()
    {
        var (ledger, _) = Make();

        ledger.Ingest(Feed(Incident("A1")));

        Assert.Equal(IncidentStage.Created, ledger.Find("A1")!.Stage);
    }

    [Fact]
    public void Ingest_NewIncidentWithUnits_IsAssigned()
    {
        var (ledger, _) = Make();

        ledger.Ingest(Feed(Incident("A1", "A008")));

        var record = ledger.Find("A1")!;

        Assert.Equal(IncidentStage.Assigned, record.Stage);

        Assert.NotNull(record.AssignedUtc);
    }

    [Fact]
    public void Ingest_UnitsAddedLater_MovesCreatedToAssigned()
    {
        var (ledger, _) = Make();

        ledger.Ingest(Feed(Incident("A1")));

        ledger.Ingest(Feed(Incident("A1", "M074")));

        Assert.Equal(IncidentStage.Assigned, ledger.Find("A1")!.Stage);
    }

    [Fact]
    public void Ingest_UnitsRemovedByCity_KeepsEveryUnitEverAssigned()
    {
        var (ledger, _) = Make();

        ledger.Ingest(Feed(Incident("A1", "A008", "E007")));

        ledger.Ingest(Feed(Incident("A1", "E007")));

        Assert.Equal(["A008", "E007"], ledger.Find("A1")!.Units);
    }

    [Fact]
    public void Ingest_CityClosesUnattendedIncident_ClearsItAtOnce()
    {
        var (ledger, _) = Make();

        ledger.Ingest(Feed(Incident("A1")));

        ledger.Ingest(new FeedRequest { Closed = ["A1"] });

        Assert.Equal(IncidentStage.Cleared, ledger.Find("A1")!.Stage);
    }

    [Fact]
    public void Ingest_CityClosesAssignedIncident_HandlesItButDoesNotClearIt()
    {
        var (ledger, _) = Make();

        ledger.Ingest(Feed(Incident("A1", "A008")));

        ledger.Ingest(new FeedRequest { Closed = ["A1"] });

        Assert.Equal(IncidentStage.Handled, ledger.Find("A1")!.Stage);
    }

    [Fact]
    public void Tick_HandledIncidentBeforeHoldEnds_StaysHandled()
    {
        var (ledger, clock) = Make();

        ledger.Ingest(Feed(Incident("A1", "A008")));

        ledger.Ingest(new FeedRequest { Closed = ["A1"] });

        clock.Advance(TimeSpan.FromSeconds(119));

        ledger.Tick();

        Assert.Equal(IncidentStage.Handled, ledger.Find("A1")!.Stage);
    }

    [Fact]
    public void Tick_HandledIncidentAfterHoldEnds_IsCleared()
    {
        var (ledger, clock) = Make();

        ledger.Ingest(Feed(Incident("A1", "A008")));

        ledger.Ingest(new FeedRequest { Closed = ["A1"] });

        clock.Advance(TimeSpan.FromSeconds(120));

        ledger.Tick();

        var record = ledger.Find("A1")!;

        Assert.Equal(IncidentStage.Cleared, record.Stage);

        Assert.NotNull(record.ClearedUtc);
    }

    [Fact]
    public void Ingest_IncidentStillListedAfterClear_StaysCleared()
    {
        var (ledger, _) = Make();

        ledger.Ingest(Feed(Incident("A1", "A008")));

        ledger.Clear("A1", "cleared");

        ledger.Ingest(Feed(Incident("A1", "A008", "E007")));

        Assert.Equal(IncidentStage.Cleared, ledger.Find("A1")!.Stage);
    }

    [Fact]
    public void Assign_UnknownIncident_CreatesItAssigned()
    {
        var (ledger, _) = Make();

        var record = ledger.Assign("G1", new AssignRequest { Units = ["Ambulance 16"], IncidentType = "MEDICAL", Address = "1 Main St" });

        Assert.Equal(IncidentStage.Assigned, record.Stage);

        Assert.Equal("MEDICAL", record.IncidentType);
    }

    [Fact]
    public void Clear_AssignedIncident_RecordsOutcome()
    {
        var (ledger, _) = Make();

        ledger.Assign("G1", new AssignRequest { Units = ["Ambulance 16"] });

        Assert.True(ledger.Clear("G1", "cleared"));

        Assert.Equal("cleared", ledger.Find("G1")!.Outcome);
    }

    [Fact]
    public void Clear_UnknownIncident_ReturnsFalse()
    {
        var (ledger, _) = Make();

        Assert.False(ledger.Clear("nope", "cleared"));
    }

    [Fact]
    public void Handle_ClearedIncident_DoesNotMoveBackward()
    {
        var (ledger, _) = Make();

        ledger.Assign("G1", new AssignRequest { Units = ["Ambulance 16"] });

        ledger.Clear("G1", "cleared");

        ledger.Handle("G1", "late");

        Assert.Equal(IncidentStage.Cleared, ledger.Find("G1")!.Stage);
    }

    [Fact]
    public void Snapshot_FromZero_ReturnsOnlyActiveIncidents()
    {
        var (ledger, _) = Make();

        ledger.Ingest(Feed(Incident("A1"), Incident("A2", "A008"), Incident("A3", "A009")));

        ledger.Clear("A3", "cleared");

        var snapshot = ledger.Snapshot(0);

        Assert.Equal(["A1", "A2"], snapshot.Incidents.Select(item => item.IncidentId).Order().ToArray());

        Assert.Empty(snapshot.Cleared);
    }

    [Fact]
    public void Snapshot_SinceRevision_ReportsClearedIds()
    {
        var (ledger, _) = Make();

        ledger.Ingest(Feed(Incident("A1", "A008")));

        var seen = ledger.Revision;

        ledger.Clear("A1", "cleared");

        var snapshot = ledger.Snapshot(seen);

        Assert.Equal(["A1"], snapshot.Cleared);

        Assert.Equal(["A1"], snapshot.Closed);

        Assert.Empty(snapshot.Incidents);
    }

    [Fact]
    public void Snapshot_SinceAheadOfLedger_ReturnsEverythingActive()
    {
        var (ledger, _) = Make();

        ledger.Ingest(Feed(Incident("A1")));

        var snapshot = ledger.Snapshot(9999);

        Assert.Single(snapshot.Incidents);
    }

    [Fact]
    public void Restore_SavedLedger_KeepsAssignedIncidentsAndRevision()
    {
        var (first, _) = Make();

        first.Ingest(Feed(Incident("A1", "A008"), Incident("A2")));

        var saved = first.TakeUnsaved()!;

        var (second, _) = Make();

        second.Restore(saved);

        Assert.Equal(first.Revision, second.Revision);

        Assert.Equal(IncidentStage.Assigned, second.Find("A1")!.Stage);

        Assert.Equal(IncidentStage.Created, second.Find("A2")!.Stage);
    }

    [Fact]
    public void TakeUnsaved_NothingChanged_ReturnsNull()
    {
        var (ledger, _) = Make();

        ledger.Ingest(Feed(Incident("A1")));

        Assert.NotNull(ledger.TakeUnsaved());

        Assert.Null(ledger.TakeUnsaved());
    }

    [Fact]
    public void Tick_MoreClearedThanHistoryLimit_DropsOldest()
    {
        var (ledger, clock) = Make(new ServiceOptions { MaximumClearedHistory = 2 });

        foreach (var id in new[] { "A1", "A2", "A3" })
        {
            ledger.Assign(id, new AssignRequest { Units = ["U"] });

            ledger.Clear(id, "cleared");

            clock.Advance(TimeSpan.FromSeconds(1));
        }

        ledger.Tick();

        Assert.Null(ledger.Find("A1"));

        Assert.NotNull(ledger.Find("A2"));

        Assert.NotNull(ledger.Find("A3"));
    }

    private static (IncidentLedger Ledger, TestClock Clock) Make(ServiceOptions? settings = null)
    {
        var clock = new TestClock();

        var ledger = new IncidentLedger(clock, Microsoft.Extensions.Options.Options.Create(settings ?? new ServiceOptions()));

        return (ledger, clock);
    }

    private static FeedIncident Incident(string id, params string[] units)
    {
        return new FeedIncident { IncidentId = id, IncidentType = "EMS EVENT", Address = "1 MAIN ST", Units = [.. units], Latitude = 29.74, Longitude = -95.37 };
    }

    private static FeedRequest Feed(params FeedIncident[] incidents)
    {
        return new FeedRequest { Incidents = [.. incidents] };
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
