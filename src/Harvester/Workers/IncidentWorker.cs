namespace HoustonIncidents.Harvester.Workers;

/// <summary>
/// Polls the Active Incidents page on a timer and prints NEW / CLOSED lines to the console.
/// </summary>
/// <param name="pageClient">Fetches the page HTML.</param>
/// <param name="parser">Extracts incidents from the HTML.</param>
/// <param name="tracker">Diffs successive snapshots.</param>
/// <param name="options">Supplies the poll interval.</param>
/// <param name="logger">Logs skipped polls and lifecycle events.</param>
internal sealed class IncidentWorker(
    IIncidentPageClient pageClient,
    IIncidentTableParser parser,
    IIncidentTracker tracker,
    IOptions<IncidentOptions> options,
    ILogger<IncidentWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds));

        logger.LogInformation("Harvester started; polling every {PollIntervalSeconds}s", options.Value.PollIntervalSeconds);

        try
        {
            do
            {
                await PollAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Harvester stopping");
        }
    }

    /// <summary>
    /// Runs one fetch-parse-diff-print cycle. A failed fetch or parse is logged and skipped so the
    /// tracked set is never wiped by a bad poll.
    /// </summary>
    /// <param name="cancellationToken">Cancels the poll.</param>
    /// <returns>A task that completes when the poll finishes.</returns>
    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var page = await pageClient.GetPageAsync(cancellationToken).ConfigureAwait(false);

        if (!page.IsSuccess)
        {
            logger.LogWarning("Poll skipped: {Error}", page.Error);

            return;
        }

        var parsed = parser.Parse(page.Value);

        if (!parsed.IsSuccess)
        {
            logger.LogWarning("Poll skipped: {Error}", parsed.Error);

            return;
        }

        var now = DateTimeOffset.Now;

        foreach (var change in tracker.Apply(parsed.Value))
        {
            Console.WriteLine(IncidentChangeFormatter.Format(change, now));
        }
    }
}
