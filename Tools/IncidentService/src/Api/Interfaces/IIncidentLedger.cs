namespace IncidentService.Api.Interfaces;

/// <summary>
/// The incident lifecycle: created, assigned, handled, cleared. The one place that decides when an incident may go away.
/// </summary>
internal interface IIncidentLedger
{
    /// <summary>Gets the current revision; it rises with every change.</summary>
    long Revision { get; }

    /// <summary>Replaces the ledger with saved records (at start-up).</summary>
    /// <param name="file">The saved ledger.</param>
    void Restore(LedgerFile file);

    /// <summary>Applies one harvester poll: creates and updates incidents, and handles the ones the City closed.</summary>
    /// <param name="feed">The poll.</param>
    void Ingest(FeedRequest feed);

    /// <summary>Assigns units to an incident, creating the incident when it is new to the service.</summary>
    /// <param name="id">The incident id.</param>
    /// <param name="request">The units and, for a new incident, its description.</param>
    /// <returns>A copy of the record.</returns>
    LedgerRecord Assign(string id, AssignRequest request);

    /// <summary>Marks an incident handled; it clears after the hold.</summary>
    /// <param name="id">The incident id.</param>
    /// <param name="outcome">How it ended.</param>
    /// <returns>False when the incident is unknown.</returns>
    bool Handle(string id, string outcome);

    /// <summary>Clears an incident now.</summary>
    /// <param name="id">The incident id.</param>
    /// <param name="outcome">How it ended.</param>
    /// <returns>False when the incident is unknown.</returns>
    bool Clear(string id, string outcome);

    /// <summary>Clears handled incidents whose hold is over and trims old history.</summary>
    void Tick();

    /// <summary>Finds an incident.</summary>
    /// <param name="id">The incident id.</param>
    /// <returns>A copy of the record, or null.</returns>
    LedgerRecord? Find(string id);

    /// <summary>Reads what changed since a revision (everything still active when it is 0 or ahead of the ledger).</summary>
    /// <param name="since">The revision the caller last saw.</param>
    /// <returns>The changes.</returns>
    SnapshotResponse Snapshot(long since);

    /// <summary>Takes a copy of the ledger to save, or null when nothing changed since the last take.</summary>
    /// <returns>The copy, or null.</returns>
    LedgerFile? TakeUnsaved();
}
