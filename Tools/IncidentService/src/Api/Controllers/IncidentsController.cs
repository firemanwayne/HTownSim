namespace IncidentService.Api.Controllers;

/// <summary>
/// The incident service API. The harvester posts feeds; the game reads changes and reports assignments and outcomes.
/// </summary>
/// <param name="ledger">The incident lifecycle.</param>
[ApiController]
[Route("api")]
internal sealed class IncidentsController(IIncidentLedger ledger) : ControllerBase
{
    /// <summary>Reports that the service is up and the ledger revision.</summary>
    /// <returns>The revision.</returns>
    [HttpGet("health")]
    public IActionResult Health() => new OkObjectResult(new { status = "ok", revision = ledger.Revision });

    /// <summary>Takes one harvester poll.</summary>
    /// <param name="feed">New and updated incidents, and closed ids.</param>
    /// <returns>The ledger revision after the poll.</returns>
    [HttpPost("feed")]
    public IActionResult PostFeed([FromBody] FeedRequest feed)
    {
        ledger.Ingest(feed);

        return new OkObjectResult(new { revision = ledger.Revision });
    }

    /// <summary>Reads what changed since a revision.</summary>
    /// <param name="since">The revision the caller last saw; 0 for everything still active.</param>
    /// <returns>The changes in the game's feed shape.</returns>
    [HttpGet("incidents")]
    public IActionResult GetChanges([FromQuery] long since = 0) => new OkObjectResult(ledger.Snapshot(since));

    /// <summary>Reads one incident.</summary>
    /// <param name="id">The incident id.</param>
    /// <returns>The record, or not found.</returns>
    [HttpGet("incidents/{id}")]
    public IActionResult GetIncident(string id)
    {
        var record = ledger.Find(id);

        return record is null ? new NotFoundObjectResult(new { error = $"Unknown incident {id}" }) : new OkObjectResult(record);
    }

    /// <summary>Assigns units to an incident (creating it when the service has not seen it).</summary>
    /// <param name="id">The incident id.</param>
    /// <param name="request">The units and, for a new incident, its description.</param>
    /// <returns>The record.</returns>
    [HttpPost("incidents/{id}/assign")]
    public IActionResult Assign(string id, [FromBody] AssignRequest request) => new OkObjectResult(ledger.Assign(id, request));

    /// <summary>Marks an incident handled.</summary>
    /// <param name="id">The incident id.</param>
    /// <param name="request">How it ended.</param>
    /// <returns>The record, or not found.</returns>
    [HttpPost("incidents/{id}/handled")]
    public IActionResult Handled(string id, [FromBody] OutcomeRequest request)
    {
        return ledger.Handle(id, request.Outcome) ? new OkObjectResult(ledger.Find(id)) : new NotFoundObjectResult(new { error = $"Unknown incident {id}" });
    }

    /// <summary>Clears an incident.</summary>
    /// <param name="id">The incident id.</param>
    /// <param name="request">How it ended.</param>
    /// <returns>The record, or not found.</returns>
    [HttpPost("incidents/{id}/clear")]
    public IActionResult Clear(string id, [FromBody] OutcomeRequest request)
    {
        return ledger.Clear(id, request.Outcome) ? new OkObjectResult(ledger.Find(id)) : new NotFoundObjectResult(new { error = $"Unknown incident {id}" });
    }
}
