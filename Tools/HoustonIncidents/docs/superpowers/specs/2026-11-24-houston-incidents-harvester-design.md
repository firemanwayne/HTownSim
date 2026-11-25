# Houston Active Incidents Harvester — Design

## Goal

A headless .NET console app that polls
`https://cohweb.houstontx.gov/ActiveIncidents/Combined.aspx?agency=%25`,
reads only the `GridView2` table, and prints incident changes to the console.

## Source page facts

- Server-rendered ASP.NET WebForms page; plain GET returns every row (no paging, no login, no JS needed).
- `GridView2` columns: Agency, Address, Cross Street, Key Map, Call Time(Opened), Incident Type, Combined Response.
- Page has no incident ID.

## Data model

`Models/Incident` record — only the fields that matter:

| Field | Source column |
|---|---|
| `Agency` | Agency |
| `Address` | Address |
| `IncidentType` | Incident Type |
| `CallTimeOpened` | Call Time(Opened) |

Identity of an incident = all four fields together. Other columns are ignored.

## Behavior

- Poll on a fixed interval (default 30 s, configurable).
- First successful poll is a baseline: all current rows are printed as `NEW`.
- Each later poll:
  - row present now, absent before -> **NEW** (added to tracked set)
  - row absent now, present before -> **CLOSED** (removed from tracked set)
- Output is console only, e.g.
  `[09:33:12] NEW       FD | AUTUMN FOREST DR | EMS EVENT | 11/24/2026 09:33`
  `[09:41:12] CLOSED    FD | AUTUMN FOREST DR | EMS EVENT | 11/24/2026 09:33`
- A failed poll (HTTP error, timeout, or `GridView2` missing) is logged and skipped. It must never be treated as an empty table, which would wrongly close every incident.

## Components (one type per file)

- `Services/IncidentPageClient` — `HttpClient` GET, browser-like User-Agent, timeout.
- `Services/IncidentTableParser` — HtmlAgilityPack, selects `//table[@id='GridView2']`, skips the header row, locates columns by header text, trims and HTML-decodes. Returns a failure result if the table is absent.
- `Services/IncidentTracker` — keeps the tracked set, diffs each snapshot, returns NEW/CLOSED events.
- `Worker` (`BackgroundService`) — `PeriodicTimer` loop; prints events; structured logging.
- `IncidentOptions` via `IOptions<T>` — URL, poll interval, request timeout.
- Registration through `ServiceRegistryExtensions.AddIncidentServices()`.

## Testing

- `HoustonIncidents.UnitTests`: parser tests against a saved copy of the page (including a page with no `GridView2`); tracker tests for new, closed, unchanged, and baseline cases.

## Out of scope

Persistence, other columns, notifications, other agencies' filters (the `%` agency filter returns all).
