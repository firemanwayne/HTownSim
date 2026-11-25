namespace IncidentService.Api.Services;

/// <summary>
/// The incident lifecycle, in memory and thread-safe. Created becomes Assigned when a unit is assigned; once Assigned, the City
/// closing the call only moves it to Handled, and it is Cleared after the hold (or when the game clears it). A Created incident the
/// City closes was never attended, so it clears at once.
/// </summary>
/// <param name="clock">The time source.</param>
/// <param name="options">Supplies the hold and the history size.</param>
internal sealed class IncidentLedger(TimeProvider clock, IOptions<ServiceOptions> options) : IIncidentLedger
{
    /// <summary>Why a never-attended incident cleared.</summary>
    private const string ClosedUnattended = "closed by City, no unit assigned";

    /// <summary>Why an attended incident was handled.</summary>
    private const string ClosedAttended = "closed by City";

    /// <summary>Why a handled incident cleared.</summary>
    private const string HoldElapsed = "cleared after hold";

    private readonly Lock _gate = new();

    private readonly Dictionary<string, LedgerRecord> _records = new(StringComparer.Ordinal);

    private long _revision;

    private bool _unsaved;

    /// <inheritdoc />
    public long Revision
    {
        get
        {
            lock (_gate)
            {
                return _revision;
            }
        }
    }

    /// <inheritdoc />
    public void Restore(LedgerFile file)
    {
        lock (_gate)
        {
            _records.Clear();

            foreach (var record in file.Records.Where(item => item.IncidentId.Length > 0))
            {
                _records[record.IncidentId] = record;
            }

            _revision = _records.Count == 0 ? 0 : _records.Values.Max(record => record.Revision);

            _unsaved = false;
        }
    }

    /// <inheritdoc />
    public void Ingest(FeedRequest feed)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();

            foreach (var incident in feed.Incidents.Where(item => item.IncidentId.Length > 0))
            {
                IngestIncident(incident, now);
            }

            foreach (var id in feed.Closed)
            {
                CloseByCity(id, now);
            }
        }
    }

    /// <inheritdoc />
    public LedgerRecord Assign(string id, AssignRequest request)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();

            if (!_records.TryGetValue(id, out var record))
            {
                record = new LedgerRecord
                {
                    IncidentId = id,
                    IncidentType = request.IncidentType,
                    Address = request.Address,
                    Latitude = request.Latitude,
                    Longitude = request.Longitude,
                    CreatedUtc = now,
                };

                _records[id] = record;

                Touch(record);
            }

            if (AddUnits(record, request.Units))
            {
                Touch(record);
            }

            if (record.Stage == IncidentStage.Created && record.Units.Count > 0)
            {
                Advance(record, IncidentStage.Assigned, string.Empty, now);
            }

            return record.Clone();
        }
    }

    /// <inheritdoc />
    public bool Handle(string id, string outcome)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(id, out var record))
            {
                return false;
            }

            Advance(record, IncidentStage.Handled, outcome, clock.GetUtcNow());

            return true;
        }
    }

    /// <inheritdoc />
    public bool Clear(string id, string outcome)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(id, out var record))
            {
                return false;
            }

            Advance(record, IncidentStage.Cleared, outcome, clock.GetUtcNow());

            return true;
        }
    }

    /// <inheritdoc />
    public void Tick()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();

            var hold = TimeSpan.FromSeconds(options.Value.HandledHoldSeconds);

            var due = _records.Values
                .Where(record => record.Stage == IncidentStage.Handled && record.HandledUtc is { } handled && now - handled >= hold)
                .ToList();

            foreach (var record in due)
            {
                Advance(record, IncidentStage.Cleared, HoldElapsed, now);
            }

            TrimHistory();
        }
    }

    /// <inheritdoc />
    public LedgerRecord? Find(string id)
    {
        lock (_gate)
        {
            return _records.TryGetValue(id, out var record) ? record.Clone() : null;
        }
    }

    /// <inheritdoc />
    public SnapshotResponse Snapshot(long since)
    {
        lock (_gate)
        {
            var full = since <= 0 || since > _revision;

            var response = new SnapshotResponse { Revision = _revision };

            var changed = _records.Values
                .Where(record => full ? record.Stage <= IncidentStage.Assigned : record.Revision > since)
                .OrderBy(record => record.Revision);

            foreach (var record in changed)
            {
                if (record.Stage <= IncidentStage.Assigned)
                {
                    response.Incidents.Add(ToFeed(record));

                    continue;
                }

                response.Closed.Add(record.IncidentId);

                if (record.Stage == IncidentStage.Cleared)
                {
                    response.Cleared.Add(record.IncidentId);
                }
            }

            return response;
        }
    }

    /// <inheritdoc />
    public LedgerFile? TakeUnsaved()
    {
        lock (_gate)
        {
            if (!_unsaved)
            {
                return null;
            }

            _unsaved = false;

            return new LedgerFile { Records = [.. _records.Values.OrderBy(record => record.Revision).Select(record => record.Clone())] };
        }
    }

    /// <summary>Creates or updates one harvested incident. A cleared incident stays cleared even if the City still lists it.</summary>
    /// <param name="incident">The harvested incident.</param>
    /// <param name="now">The time.</param>
    private void IngestIncident(FeedIncident incident, DateTimeOffset now)
    {
        if (!_records.TryGetValue(incident.IncidentId, out var record))
        {
            record = new LedgerRecord { IncidentId = incident.IncidentId, CreatedUtc = now };

            _records[incident.IncidentId] = record;

            Describe(record, incident);

            AddUnits(record, incident.Units);

            Touch(record);
        }
        else if (record.Stage == IncidentStage.Cleared)
        {
            return;
        }
        else
        {
            var described = Describe(record, incident);

            var added = AddUnits(record, incident.Units);

            if (described || added)
            {
                Touch(record);
            }
        }

        if (record.Stage == IncidentStage.Created && record.Units.Count > 0)
        {
            Advance(record, IncidentStage.Assigned, string.Empty, now);
        }
    }

    /// <summary>Handles an incident the City closed: attended ones are handled, unattended ones clear.</summary>
    /// <param name="id">The incident id.</param>
    /// <param name="now">The time.</param>
    private void CloseByCity(string id, DateTimeOffset now)
    {
        if (!_records.TryGetValue(id, out var record))
        {
            return;
        }

        if (record.Stage == IncidentStage.Created)
        {
            Advance(record, IncidentStage.Cleared, ClosedUnattended, now);
        }
        else if (record.Stage == IncidentStage.Assigned)
        {
            Advance(record, IncidentStage.Handled, ClosedAttended, now);
        }
    }

    /// <summary>Copies the descriptive fields of a harvested incident onto a record; true when anything changed.</summary>
    /// <param name="record">The record.</param>
    /// <param name="incident">The harvested incident.</param>
    /// <returns>True when a field changed.</returns>
    private static bool Describe(LedgerRecord record, FeedIncident incident)
    {
        var changed = record.Agency != incident.Agency
            || record.IncidentType != incident.IncidentType
            || record.Address != incident.Address
            || record.CrossStreet != incident.CrossStreet
            || record.KeyMap != incident.KeyMap
            || record.CallTimeOpened != incident.CallTimeOpened
            || record.AlarmLevel != incident.AlarmLevel
            || (incident.Latitude != 0d && record.Latitude != incident.Latitude)
            || (incident.Longitude != 0d && record.Longitude != incident.Longitude);

        record.Agency = incident.Agency;

        record.IncidentType = incident.IncidentType;

        record.Address = incident.Address;

        record.CrossStreet = incident.CrossStreet;

        record.KeyMap = incident.KeyMap;

        record.CallTimeOpened = incident.CallTimeOpened;

        record.AlarmLevel = incident.AlarmLevel;

        if (incident.Latitude != 0d || incident.Longitude != 0d)
        {
            record.Latitude = incident.Latitude;

            record.Longitude = incident.Longitude;
        }

        return changed;
    }

    /// <summary>Adds units not listed yet (case-insensitive); true when any were new.</summary>
    /// <param name="record">The record.</param>
    /// <param name="units">The units to add.</param>
    /// <returns>True when a unit was added.</returns>
    private static bool AddUnits(LedgerRecord record, IEnumerable<string> units)
    {
        var added = false;

        foreach (var unit in units.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (!record.Units.Contains(unit, StringComparer.OrdinalIgnoreCase))
            {
                record.Units.Add(unit);

                added = true;
            }
        }

        return added;
    }

    /// <summary>Moves a record forward to a stage and stamps its time; does nothing when it is already there or past.</summary>
    /// <param name="record">The record.</param>
    /// <param name="target">The stage to reach.</param>
    /// <param name="outcome">Why, or empty to keep the existing outcome.</param>
    /// <param name="now">The time.</param>
    private void Advance(LedgerRecord record, IncidentStage target, string outcome, DateTimeOffset now)
    {
        if (target <= record.Stage)
        {
            return;
        }

        if (target >= IncidentStage.Assigned)
        {
            record.AssignedUtc ??= now;
        }

        if (target >= IncidentStage.Handled)
        {
            record.HandledUtc ??= now;
        }

        if (target == IncidentStage.Cleared)
        {
            record.ClearedUtc = now;
        }

        record.Stage = target;

        if (outcome.Length > 0)
        {
            record.Outcome = outcome;
        }

        Touch(record);
    }

    /// <summary>Stamps a record with a new revision and flags the ledger as needing a save.</summary>
    /// <param name="record">The record that changed.</param>
    private void Touch(LedgerRecord record)
    {
        _revision++;

        record.Revision = _revision;

        _unsaved = true;
    }

    /// <summary>Drops the oldest cleared records past the history limit.</summary>
    private void TrimHistory()
    {
        var cleared = _records.Values
            .Where(record => record.Stage == IncidentStage.Cleared)
            .OrderBy(record => record.ClearedUtc)
            .ToList();

        var excess = cleared.Count - options.Value.MaximumClearedHistory;

        for (var i = 0; i < excess; i++)
        {
            _records.Remove(cleared[i].IncidentId);

            _unsaved = true;
        }
    }

    /// <summary>Builds the game-shaped incident from a record.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The feed incident.</returns>
    private static FeedIncident ToFeed(LedgerRecord record)
    {
        return new FeedIncident
        {
            IncidentId = record.IncidentId,
            Agency = record.Agency,
            IncidentType = record.IncidentType,
            Address = record.Address,
            CrossStreet = record.CrossStreet,
            KeyMap = record.KeyMap,
            CallTimeOpened = record.CallTimeOpened,
            AlarmLevel = record.AlarmLevel,
            Units = [.. record.Units],
            Latitude = record.Latitude,
            Longitude = record.Longitude,
        };
    }
}
