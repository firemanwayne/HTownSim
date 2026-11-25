namespace IncidentService.Api.Interfaces;

/// <summary>
/// Where the ledger is kept between runs.
/// </summary>
internal interface IIncidentStore
{
    /// <summary>Reads the saved ledger; empty when nothing was saved.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The ledger.</returns>
    Task<LedgerFile> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Writes the ledger.</summary>
    /// <param name="file">The ledger.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the ledger is saved.</returns>
    Task SaveAsync(LedgerFile file, CancellationToken cancellationToken);
}
