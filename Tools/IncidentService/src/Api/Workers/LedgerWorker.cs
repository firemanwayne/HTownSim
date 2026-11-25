namespace IncidentService.Api.Workers;

/// <summary>
/// Housekeeping for the ledger: loads the saved ledger at start, then on a timer clears handled incidents whose hold is over and
/// saves the ledger when it changed. Saves once more on shutdown.
/// </summary>
/// <param name="ledger">The ledger.</param>
/// <param name="store">Where the ledger is saved.</param>
/// <param name="options">Supplies the tick interval.</param>
/// <param name="logger">Logs start-up and shutdown.</param>
internal sealed partial class LedgerWorker(
    IIncidentLedger ledger,
    IIncidentStore store,
    IOptions<ServiceOptions> options,
    ILogger<LedgerWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var file = await store
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        ledger.Restore(file);

        LogRestoredInformation(logger, file.Records.Count);

        await base
            .StartAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.TickSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                ledger.Tick();

                await SaveIfChangedAsync(stoppingToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            LogStoppingInformation(logger);
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base
            .StopAsync(cancellationToken)
            .ConfigureAwait(false);

        await SaveIfChangedAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Saves the ledger when it changed since the last save.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes when the save is done.</returns>
    private async Task SaveIfChangedAsync(CancellationToken cancellationToken)
    {
        var file = ledger.TakeUnsaved();

        if (file is null)
        {
            return;
        }

        await store
            .SaveAsync(file, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Logs the restored ledger.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="count">How many records were restored.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Incident ledger restored: {Count} records")]
    private static partial void LogRestoredInformation(ILogger logger, int count);

    /// <summary>Logs shutdown.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Incident ledger stopping")]
    private static partial void LogStoppingInformation(ILogger logger);
}
