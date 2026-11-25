namespace HoustonIncidents.Harvester.Workers;

/// <summary>
/// Polls the Active Incidents pages on a timer, prints NEW / CLOSED lines to the console and hands the changes to HTownSim.
/// Each poll reads the all-agencies page (the incident list) and the fire-only page (units and alarm level for fire and EMS
/// incidents), places each new incident on the map, and writes one feed file with new incidents, incidents whose units changed,
/// and closed ids.
/// </summary>
/// <param name="pageClient">Fetches the pages.</param>
/// <param name="parser">Extracts incidents from the all-agencies page.</param>
/// <param name="fireParser">Extracts incidents (with units) from the fire-only page.</param>
/// <param name="tracker">Diffs successive snapshots.</param>
/// <param name="geocoder">Places incidents on the map.</param>
/// <param name="feedWriter">Writes the game feed files.</param>
/// <param name="options">Supplies the poll interval, the fire page URL and the game inbox.</param>
/// <param name="logger">Logs skipped polls and lifecycle events.</param>
internal sealed partial class IncidentWorker(
    IIncidentPageClient pageClient,
    IIncidentTableParser parser,
    IFireIncidentTableParser fireParser,
    IIncidentTracker tracker,
    IIncidentGeocoder geocoder,
    IGameFeedWriter feedWriter,
    IOptions<IncidentOptions> options,
    ILogger<IncidentWorker> logger) : BackgroundService
{
    /// <summary>The page agency code for fire and EMS incidents (the ones the fire page lists).</summary>
    private const string FireAgency = "FD";

    /// <summary>The game-ready record of every incident still open, by id (units refreshed each poll).</summary>
    private readonly Dictionary<string, LiveIncident> _open = [];

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds));

        LogStartedInformation(logger, options.Value.PollIntervalSeconds);

        try
        {
            do
            {
                await PollAsync(stoppingToken)
                    .ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            LogStoppingInformation(logger);
        }
    }

    /// <summary>
    /// Runs one fetch-parse-diff-print-export cycle. A failed fetch or parse of the main page is logged and skipped so the
    /// tracked set is never wiped by a bad poll; a failed fire page only means no units this time.
    /// </summary>
    /// <param name="cancellationToken">Cancels the poll.</param>
    /// <returns>A task that completes when the poll finishes.</returns>
    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var page = await pageClient
            .GetPageAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!page.IsSuccess)
        {
            LogPollSkippedWarning(logger, page.Error);

            return;
        }

        var parsed = parser.Parse(page.Value);

        if (!parsed.IsSuccess)
        {
            LogPollSkippedWarning(logger, parsed.Error);

            return;
        }

        var fire = await FireDetailsAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.Now;

        var changed = new List<LiveIncident>();

        var closed = new List<string>();

        foreach (var change in tracker.Apply(parsed.Value))
        {
            Console.WriteLine(IncidentChangeFormatter.Format(change, now));

            var id = IncidentIdFormatter.Format(change.Incident);

            if (change.Kind == IncidentChangeKind.Closed)
            {
                _open.Remove(id);

                closed.Add(id);

                continue;
            }

            var live = await DescribeAsync(new DescribeRequest(id, change.Incident, Match(fire, change.Incident)), cancellationToken)
                .ConfigureAwait(false);

            _open[id] = live;

            changed.Add(live);
        }

        // Units are added to an incident after it first appears: send it again when they change.
        foreach (var incident in parsed.Value)
        {
            var id = IncidentIdFormatter.Format(incident);

            if (!_open.TryGetValue(id, out var live) || changed.Contains(live))
            {
                continue;
            }

            var units = Units(Match(fire, incident));

            if (!units.SequenceEqual(live.Units, StringComparer.Ordinal))
            {
                live.Units = units;

                changed.Add(live);
            }
        }

        await ExportAsync(new LiveFeed { Generated = now, Incidents = changed, Closed = closed }, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads the fire-only page into a lookup by address, call time and type; empty when the page fails.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The fire page rows by address, call time and type.</returns>
    private async Task<Dictionary<(string Address, string CallTime, string Type), FireIncident>> FireDetailsAsync(CancellationToken cancellationToken)
    {
        var details = new Dictionary<(string Address, string CallTime, string Type), FireIncident>();

        var page = await pageClient
            .GetPageAsync(options.Value.FireUrl, cancellationToken)
            .ConfigureAwait(false);

        if (!page.IsSuccess)
        {
            LogFirePageWarning(logger, page.Error);

            return details;
        }

        var parsed = fireParser.Parse(page.Value);

        if (!parsed.IsSuccess)
        {
            LogFirePageWarning(logger, parsed.Error);

            return details;
        }

        foreach (var row in parsed.Value)
        {
            details[(row.Address, row.CallTimeOpened, row.IncidentType)] = row;
        }

        return details;
    }

    /// <summary>The fire page row for an incident, or null (police incidents, or not listed yet).</summary>
    /// <param name="fire">The fire page rows.</param>
    /// <param name="incident">The incident.</param>
    /// <returns>The matching row, or null.</returns>
    private static FireIncident? Match(Dictionary<(string Address, string CallTime, string Type), FireIncident> fire, Incident incident)
    {
        if (incident.Agency != FireAgency)
        {
            return null;
        }

        return fire.TryGetValue((incident.Address, incident.CallTimeOpened, incident.IncidentType), out var row) ? row : null;
    }

    /// <summary>The unit codes of a fire page row, upper case, in page order.</summary>
    /// <param name="row">The row, or null.</param>
    /// <returns>The unit codes.</returns>
    private static IReadOnlyList<string> Units(FireIncident? row)
    {
        if (row is null)
        {
            return [];
        }

        return row.Units
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(unit => unit.ToUpperInvariant())
            .ToList();
    }

    /// <summary>Builds the game record of a new incident and places it on the map.</summary>
    /// <param name="request">The id, the incident and its fire page row.</param>
    /// <param name="cancellationToken">Cancels the geocoding.</param>
    /// <returns>The game record.</returns>
    private async Task<LiveIncident> DescribeAsync(DescribeRequest request, CancellationToken cancellationToken)
    {
        var incident = request.Incident;

        var row = request.Fire;

        var live = new LiveIncident
        {
            IncidentId = request.Id,
            Agency = incident.Agency,
            IncidentType = incident.IncidentType,
            Address = incident.Address,
            CrossStreet = row?.CrossStreet ?? string.Empty,
            KeyMap = row?.KeyMap ?? string.Empty,
            CallTimeOpened = incident.CallTimeOpened,
            AlarmLevel = row?.AlarmLevel ?? string.Empty,
            Units = Units(row),
        };

        var place = await geocoder
            .LocateAsync(new GeocodeRequest(incident.Address, live.CrossStreet), cancellationToken)
            .ConfigureAwait(false);

        if (place is not null)
        {
            live.Latitude = place.Latitude;

            live.Longitude = place.Longitude;
        }

        return live;
    }

    /// <summary>Writes the feed file when there is anything to send and the game feed is on.</summary>
    /// <param name="feed">The feed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private async Task ExportAsync(LiveFeed feed, CancellationToken cancellationToken)
    {
        var exportOff = string.IsNullOrWhiteSpace(options.Value.GameInboxDirectory) && string.IsNullOrWhiteSpace(options.Value.ServiceUrl);

        if (exportOff || (feed.Incidents.Count == 0 && feed.Closed.Count == 0))
        {
            return;
        }

        var written = await feedWriter
            .WriteAsync(feed, cancellationToken)
            .ConfigureAwait(false);

        if (written.IsSuccess)
        {
            LogFeedWrittenInformation(logger, feed.Incidents.Count, feed.Closed.Count, written.Value);
        }
        else
        {
            LogFeedFailedWarning(logger, written.Error);
        }
    }

    /// <summary>Logs the start of polling.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="pollIntervalSeconds">Seconds between polls.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Harvester started; polling every {PollIntervalSeconds}s")]
    private static partial void LogStartedInformation(ILogger logger, int pollIntervalSeconds);

    /// <summary>Logs shutdown.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Harvester stopping")]
    private static partial void LogStoppingInformation(ILogger logger);

    /// <summary>Logs a skipped poll.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="error">Why the poll was skipped.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Poll skipped: {Error}")]
    private static partial void LogPollSkippedWarning(ILogger logger, string error);

    /// <summary>Logs a failed fire-page read (units unavailable this poll).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="error">Why the page failed.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Fire page unavailable, no units this poll: {Error}")]
    private static partial void LogFirePageWarning(ILogger logger, string error);

    /// <summary>Logs a feed file written for the game.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="changed">New and updated incidents in the file.</param>
    /// <param name="closed">Closed ids in the file.</param>
    /// <param name="path">The file written.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Game feed: {Changed} new/updated, {Closed} closed -> {Path}")]
    private static partial void LogFeedWrittenInformation(ILogger logger, int changed, int closed, string path);

    /// <summary>Logs a feed file that could not be written.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="error">Why the write failed.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Game feed not written: {Error}")]
    private static partial void LogFeedFailedWarning(ILogger logger, string error);
}
