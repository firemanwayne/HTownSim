# Houston Incidents Harvester Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A headless .NET console app that polls the Houston Active Incidents page, reads only the `GridView2` table, and prints `NEW` / `CLOSED` lines to the console as incidents appear and disappear.

**Architecture:** A generic-host console app with one `BackgroundService` worker. Each poll: `IncidentPageClient` fetches HTML -> `IncidentTableParser` extracts four-field `Incident` records from `GridView2` -> `IncidentTracker` diffs against the previous snapshot -> `IncidentChangeFormatter` renders each change -> worker writes it to the console. A failed fetch/parse is logged and skipped (never treated as "empty").

**Tech Stack:** .NET 10 (SDK 10.0.402 installed), HtmlAgilityPack, Microsoft.Extensions.Hosting/Http, xunit.

**Spec:** `docs/superpowers/specs/2026-11-24-houston-incidents-harvester-design.md`

**Conventions (from the user's global C# standard — follow them in every file):**
- Nullable enabled, warnings as errors, file-scoped namespaces, central package management.
- `sealed`, `internal` by default; services exposed through interfaces. One type per file.
- Folders named for suffix, pluralized (`Services/`, `Workers/`, `Formatters/`); interfaces in `Interfaces/`; DTOs in `Models/`. Namespaces mirror folders.
- `using`s live in the project's `GlobalUsings.cs`.
- Blank line after every statement inside a method body (not before a closing `}`); braces on every control-flow block.
- Every async method takes a `CancellationToken` and passes it on; `ConfigureAwait(false)` on every await.
- XML `<summary>` on types and members, including private. Test *methods* are not documented (their `MethodName_Scenario_ExpectedResult` names are the documentation); test classes are.
- Structured logging only. `nameof(...)` for member names.

**Deviation from spec:** the test project is named `HoustonIncidents.Harvester.UnitTests` (the standard says `{ProjectName}.UnitTests`; the spec's shorter name was loose).

**Known limitation:** if the page ever renders no `GridView2` at all when there are zero incidents (ASP.NET GridView omits itself with no data and no EmptyDataText), the harvester will log "table not found" and keep the last snapshot rather than close everything. That is the deliberate safe side of the spec's failed-poll rule. If you see it in practice, we can special-case it.

**NuGet versions** below are best-known; if restore reports a version not found, use the newest stable version of that package.

---

## File Structure

```
HoustonIncidents/
  HoustonIncidents.slnx
  Directory.Build.props
  Directory.Packages.props
  src/Harvester/
    HoustonIncidents.Harvester.csproj
    GlobalUsings.cs
    Program.cs
    ServiceRegistryExtensions.cs
    Models/Incident.cs
    Models/IncidentChange.cs
    Models/IncidentChangeKind.cs
    Models/Result.cs
    Options/IncidentOptions.cs
    Interfaces/IIncidentPageClient.cs
    Interfaces/IIncidentTableParser.cs
    Interfaces/IIncidentTracker.cs
    Services/IncidentPageClient.cs
    Services/IncidentTableParser.cs
    Services/IncidentTracker.cs
    Formatters/IncidentChangeFormatter.cs
    Workers/IncidentWorker.cs
  tests/UnitTests/
    HoustonIncidents.Harvester.UnitTests.csproj
    GlobalUsings.cs
    Fixtures/GridView2Sample.html
    Services/IncidentTableParserTests.cs
    Services/IncidentTrackerTests.cs
    Formatters/IncidentChangeFormatterTests.cs
```

---

### Task 1: Scaffold solution, build props, and projects

**Files:** all of the project/props files listed above (no C# types yet except `GlobalUsings.cs` and a placeholder `Program.cs`).

- [ ] **Step 1: Initialise git on a feature branch**

```bash
cd /d/source/repos/HoustonIncidents
git init
git checkout -b feature/incident-harvester
```

- [ ] **Step 2: Create `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Create `Directory.Packages.props`**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="HtmlAgilityPack" Version="1.12.1" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Http" Version="10.0.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create `src/Harvester/HoustonIncidents.Harvester.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="HtmlAgilityPack" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.Http" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="HoustonIncidents.Harvester.UnitTests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Create `src/Harvester/GlobalUsings.cs`**

```csharp
global using System;
global using System.Collections.Generic;
global using System.Diagnostics.CodeAnalysis;
global using System.Linq;
global using System.Net.Http;
global using System.Threading;
global using System.Threading.Tasks;
global using HoustonIncidents.Harvester.Formatters;
global using HoustonIncidents.Harvester.Interfaces;
global using HoustonIncidents.Harvester.Models;
global using HoustonIncidents.Harvester.Options;
global using HoustonIncidents.Harvester.Services;
global using HoustonIncidents.Harvester.Workers;
global using HtmlAgilityPack;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
```

(The namespaces don't exist yet, so the project will not build until Tasks 2-7 add them. That is expected; the build check at the end of this task uses a temporary stub.)

- [ ] **Step 6: Create `tests/UnitTests/HoustonIncidents.Harvester.UnitTests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Harvester\HoustonIncidents.Harvester.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

- [ ] **Step 7: Create `tests/UnitTests/GlobalUsings.cs`**

```csharp
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using HoustonIncidents.Harvester.Formatters;
global using HoustonIncidents.Harvester.Models;
global using HoustonIncidents.Harvester.Services;
global using Xunit;
```

- [ ] **Step 8: Create a temporary `src/Harvester/Program.cs`, the solution, and verify the restore**

Temporary stub (replaced in Task 7). Because `GlobalUsings.cs` references namespaces that don't exist yet, create empty placeholder namespaces so it builds: skip the build here and only restore.

```csharp
return 0;
```

```bash
cd /d/source/repos/HoustonIncidents
dotnet new sln --format slnx -n HoustonIncidents
dotnet sln add src/Harvester/HoustonIncidents.Harvester.csproj tests/UnitTests/HoustonIncidents.Harvester.UnitTests.csproj
dotnet restore
```

Expected: restore succeeds for both projects. (Build errors CS0234 about missing namespaces are expected until Task 2+; do not build yet.)

- [ ] **Step 9: Create `.gitignore` and commit**

```bash
cd /d/source/repos/HoustonIncidents
printf 'bin/\nobj/\n.vs/\n*.user\n' > .gitignore
git add -A
git commit -m "chore: scaffold solution and projects" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Models

**Files:**
- Create: `src/Harvester/Models/Incident.cs`, `IncidentChangeKind.cs`, `IncidentChange.cs`, `Result.cs`

- [ ] **Step 1: Create `Models/Incident.cs`**

```csharp
namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// One active incident row from the page's GridView2 table. Record equality over all four
/// fields is the incident's identity, because the page publishes no incident ID.
/// </summary>
/// <param name="Agency">Responding agency, e.g. FD or PD.</param>
/// <param name="Address">Incident address.</param>
/// <param name="IncidentType">Incident type, e.g. EMS EVENT.</param>
/// <param name="CallTimeOpened">Call-opened time exactly as shown on the page.</param>
internal sealed record Incident(string Agency, string Address, string IncidentType, string CallTimeOpened);
```

- [ ] **Step 2: Create `Models/IncidentChangeKind.cs`**

```csharp
namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// The kind of change detected between two polls.
/// </summary>
internal enum IncidentChangeKind
{
    /// <summary>The incident appeared in the latest snapshot.</summary>
    New,

    /// <summary>The incident was in the previous snapshot but is gone now.</summary>
    Closed,
}
```

- [ ] **Step 3: Create `Models/IncidentChange.cs`**

```csharp
namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// A single detected change to an incident.
/// </summary>
/// <param name="Kind">Whether the incident is new or closed.</param>
/// <param name="Incident">The incident that changed.</param>
internal sealed record IncidentChange(IncidentChangeKind Kind, Incident Incident);
```

- [ ] **Step 4: Create `Models/Result.cs`**

```csharp
namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// Framework-agnostic success-or-error result.
/// </summary>
/// <typeparam name="T">The success value type.</typeparam>
internal sealed class Result<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Result{T}"/> class.
    /// </summary>
    /// <param name="value">The success value, or default on failure.</param>
    /// <param name="error">The error message, or null on success.</param>
    private Result(T? value, string? error)
    {
        Value = value;

        Error = error;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>Gets the success value; non-null when <see cref="IsSuccess"/> is true.</summary>
    public T? Value { get; }

    /// <summary>Gets the error message; non-null when <see cref="IsSuccess"/> is false.</summary>
    public string? Error { get; }

    /// <summary>Creates a successful result.</summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful result.</returns>
    public static Result<T> Ok(T value) => new(value, null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed result.</returns>
    public static Result<T> Fail(string error) => new(default, error);
}
```

- [ ] **Step 5: Commit** (build is still blocked until the remaining namespaces exist; that's fine)

```bash
git add -A
git commit -m "feat: add incident models and Result type" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Table parser (TDD)

**Files:**
- Create: `src/Harvester/Interfaces/IIncidentTableParser.cs`, `src/Harvester/Services/IncidentTableParser.cs`
- Create: `tests/UnitTests/Fixtures/GridView2Sample.html`, `tests/UnitTests/Services/IncidentTableParserTests.cs`

Because `GlobalUsings.cs` names namespaces that don't exist yet (`Formatters`, `Options`, `Workers`), first add one empty-namespace-compatible file each is NOT possible; instead the namespaces come into existence as their first types are added. To unblock building now, create the interface and an empty `Options` placeholder in this task order: Task 3 adds `Interfaces` and `Services`; the `Formatters`, `Options`, and `Workers` namespaces are created in Tasks 5-7. **Until then, temporarily comment out the three missing `global using` lines** in `src/Harvester/GlobalUsings.cs` (`Formatters`, `Options`, `Workers`) and in `tests/UnitTests/GlobalUsings.cs` (`Formatters`), and restore each line in the task that adds its namespace (Tasks 5, 6, 7).

- [ ] **Step 1: Comment out the missing global usings** (as described above), then create the interface

`Interfaces/IIncidentTableParser.cs`:

```csharp
namespace HoustonIncidents.Harvester.Interfaces;

/// <summary>
/// Extracts incidents from the Active Incidents page HTML.
/// </summary>
internal interface IIncidentTableParser
{
    /// <summary>
    /// Parses the GridView2 table in the supplied page HTML.
    /// </summary>
    /// <param name="html">The full page HTML.</param>
    /// <returns>The incidents, or a failure if the table or a required column is missing.</returns>
    Result<IReadOnlyList<Incident>> Parse(string html);
}
```

- [ ] **Step 2: Create the fixture `tests/UnitTests/Fixtures/GridView2Sample.html`**

```html
<html>
<body>
<table id="Other"><tr><td>ignore me</td><td>ignore</td><td>ignore</td><td>ignore</td></tr></table>
<form>
<table id="GridView2" border="1">
  <tr>
    <th scope="col"><a href="#">Agency</a></th>
    <th scope="col"><a href="#">Address</a></th>
    <th scope="col"><a href="#">Cross Street</a></th>
    <th scope="col"><a href="#">Key Map</a></th>
    <th scope="col"><a href="#">Call Time(Opened)</a></th>
    <th scope="col"><a href="#">Incident Type</a></th>
    <th scope="col"><a href="#">Combined Response</a></th>
  </tr>
  <tr>
    <td>FD</td><td>AUTUMN FOREST DR</td><td>BLK SILVER FOREST DR</td><td>451B</td><td>11/24/2026 09:33</td><td>EMS EVENT</td><td>N</td>
  </tr>
  <tr>
    <td>PD</td><td>  MAIN &amp; ELM&nbsp;ST </td><td>&nbsp;</td><td>492A</td><td>11/24/2026 09:35</td><td>DISTURBANCE</td><td>Y</td>
  </tr>
</table>
</form>
</body>
</html>
```

- [ ] **Step 3: Write the failing tests `tests/UnitTests/Services/IncidentTableParserTests.cs`**

```csharp
namespace HoustonIncidents.Harvester.UnitTests.Services;

/// <summary>
/// Tests for <see cref="IncidentTableParser"/>.
/// </summary>
public sealed class IncidentTableParserTests
{
    [Fact]
    public void Parse_GridViewPresent_ReturnsFourFieldIncidents()
    {
        var parser = new IncidentTableParser();

        var result = parser.Parse(LoadFixture("GridView2Sample.html"));

        Assert.True(result.IsSuccess);

        Assert.Equal(2, result.Value.Count);

        Assert.Equal(new Incident("FD", "AUTUMN FOREST DR", "EMS EVENT", "11/24/2026 09:33"), result.Value[0]);
    }

    [Fact]
    public void Parse_EntitiesAndWhitespace_AreDecodedAndCollapsed()
    {
        var parser = new IncidentTableParser();

        var result = parser.Parse(LoadFixture("GridView2Sample.html"));

        Assert.True(result.IsSuccess);

        Assert.Equal("MAIN & ELM ST", result.Value[1].Address);
    }

    [Fact]
    public void Parse_OtherTablesPresent_OnlyGridViewIsRead()
    {
        var parser = new IncidentTableParser();

        var result = parser.Parse(LoadFixture("GridView2Sample.html"));

        Assert.True(result.IsSuccess);

        Assert.DoesNotContain(result.Value, incident => incident.Agency == "ignore me");
    }

    [Fact]
    public void Parse_NoGridView_ReturnsFailure()
    {
        var parser = new IncidentTableParser();

        var result = parser.Parse("<html><body><p>Service unavailable</p></body></html>");

        Assert.False(result.IsSuccess);

        Assert.Contains("GridView2", result.Error);
    }

    [Fact]
    public void Parse_MissingRequiredColumn_ReturnsFailure()
    {
        var parser = new IncidentTableParser();

        var html = "<table id=\"GridView2\"><tr><th>Agency</th><th>Address</th></tr></table>";

        var result = parser.Parse(html);

        Assert.False(result.IsSuccess);

        Assert.Contains("Incident Type", result.Error);
    }

    [Fact]
    public void Parse_HeaderOnlyTable_ReturnsEmptySuccess()
    {
        var parser = new IncidentTableParser();

        var html = "<table id=\"GridView2\"><tr><th>Agency</th><th>Address</th><th>Incident Type</th><th>Call Time(Opened)</th></tr></table>";

        var result = parser.Parse(html);

        Assert.True(result.IsSuccess);

        Assert.Empty(result.Value);
    }

    private static string LoadFixture(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/UnitTests/HoustonIncidents.Harvester.UnitTests.csproj`
Expected: build FAIL — `CS0246: The type or namespace name 'IncidentTableParser' could not be found`.

- [ ] **Step 5: Implement `Services/IncidentTableParser.cs`**

```csharp
namespace HoustonIncidents.Harvester.Services;

/// <summary>
/// Parses the GridView2 table of the Active Incidents page using HtmlAgilityPack.
/// </summary>
internal sealed class IncidentTableParser : IIncidentTableParser
{
    /// <summary>The HTML id of the incidents table.</summary>
    private const string TableId = "GridView2";

    /// <summary>Header text of the Agency column.</summary>
    private const string AgencyHeader = "Agency";

    /// <summary>Header text of the Address column.</summary>
    private const string AddressHeader = "Address";

    /// <summary>Header text of the Incident Type column.</summary>
    private const string IncidentTypeHeader = "Incident Type";

    /// <summary>Header text of the Call Time(Opened) column.</summary>
    private const string CallTimeHeader = "Call Time(Opened)";

    /// <inheritdoc />
    public Result<IReadOnlyList<Incident>> Parse(string html)
    {
        var document = new HtmlDocument();

        document.LoadHtml(html);

        var table = document.GetElementbyId(TableId);

        if (table is null)
        {
            return Result<IReadOnlyList<Incident>>.Fail($"Table '{TableId}' not found in page.");
        }

        var rows = table.Descendants("tr").ToList();

        var headerRow = rows.FirstOrDefault(row => row.Elements("th").Any());

        if (headerRow is null)
        {
            return Result<IReadOnlyList<Incident>>.Fail($"Table '{TableId}' has no header row.");
        }

        var headers = headerRow.Elements("th").Select(cell => Clean(cell.InnerText)).ToList();

        var agencyIndex = headers.IndexOf(AgencyHeader);

        var addressIndex = headers.IndexOf(AddressHeader);

        var typeIndex = headers.IndexOf(IncidentTypeHeader);

        var timeIndex = headers.IndexOf(CallTimeHeader);

        var missing = new[]
        {
            (AgencyHeader, agencyIndex),
            (AddressHeader, addressIndex),
            (IncidentTypeHeader, typeIndex),
            (CallTimeHeader, timeIndex),
        }.Where(column => column.Item2 < 0).Select(column => column.Item1).ToList();

        if (missing.Count > 0)
        {
            return Result<IReadOnlyList<Incident>>.Fail($"Table '{TableId}' is missing column(s): {string.Join(", ", missing)}.");
        }

        var maxIndex = new[] { agencyIndex, addressIndex, typeIndex, timeIndex }.Max();

        var incidents = new List<Incident>();

        foreach (var row in rows)
        {
            var cells = row.Elements("td").Select(cell => Clean(cell.InnerText)).ToList();

            if (cells.Count <= maxIndex)
            {
                continue;
            }

            incidents.Add(new Incident(cells[agencyIndex], cells[addressIndex], cells[typeIndex], cells[timeIndex]));
        }

        return Result<IReadOnlyList<Incident>>.Ok(incidents);
    }

    /// <summary>
    /// Decodes HTML entities and collapses runs of whitespace (including non-breaking spaces) to single spaces.
    /// </summary>
    /// <param name="text">The raw cell text.</param>
    /// <returns>The cleaned text.</returns>
    private static string Clean(string text)
    {
        var decoded = HtmlEntity.DeEntitize(text);

        return string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/UnitTests/HoustonIncidents.Harvester.UnitTests.csproj`
Expected: 6 passed. If `Parse_GridViewPresent...` returns 0 rows or wrong rows, HtmlAgilityPack's `<form>` handling is the likely cause — add `HtmlNode.ElementsFlags.Remove("form");` as the first statement of `Parse` and re-run.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: parse GridView2 into four-field incidents" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Tracker (TDD)

**Files:**
- Create: `src/Harvester/Interfaces/IIncidentTracker.cs`, `src/Harvester/Services/IncidentTracker.cs`
- Create: `tests/UnitTests/Services/IncidentTrackerTests.cs`

- [ ] **Step 1: Create `Interfaces/IIncidentTracker.cs`**

```csharp
namespace HoustonIncidents.Harvester.Interfaces;

/// <summary>
/// Tracks the set of open incidents between polls and reports what changed.
/// </summary>
internal interface IIncidentTracker
{
    /// <summary>
    /// Compares the latest snapshot with the previous one and updates the tracked set.
    /// </summary>
    /// <param name="snapshot">All incidents currently shown on the page.</param>
    /// <returns>CLOSED changes first, then NEW changes in page order.</returns>
    IReadOnlyList<IncidentChange> Apply(IReadOnlyList<Incident> snapshot);
}
```

- [ ] **Step 2: Write the failing tests `tests/UnitTests/Services/IncidentTrackerTests.cs`**

```csharp
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
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/UnitTests/HoustonIncidents.Harvester.UnitTests.csproj`
Expected: build FAIL — `CS0246 ... 'IncidentTracker'`.

- [ ] **Step 4: Implement `Services/IncidentTracker.cs`**

```csharp
namespace HoustonIncidents.Harvester.Services;

/// <summary>
/// In-memory tracker that diffs consecutive snapshots of the incident table.
/// </summary>
internal sealed class IncidentTracker : IIncidentTracker
{
    /// <summary>The incidents seen in the previous snapshot.</summary>
    private HashSet<Incident> _tracked = [];

    /// <inheritdoc />
    public IReadOnlyList<IncidentChange> Apply(IReadOnlyList<Incident> snapshot)
    {
        var current = new HashSet<Incident>(snapshot);

        var changes = new List<IncidentChange>();

        foreach (var incident in _tracked)
        {
            if (!current.Contains(incident))
            {
                changes.Add(new IncidentChange(IncidentChangeKind.Closed, incident));
            }
        }

        foreach (var incident in snapshot.Distinct())
        {
            if (!_tracked.Contains(incident))
            {
                changes.Add(new IncidentChange(IncidentChangeKind.New, incident));
            }
        }

        _tracked = current;

        return changes;
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/UnitTests/HoustonIncidents.Harvester.UnitTests.csproj`
Expected: 13 passed (6 parser + 7 tracker).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: track incident changes between polls" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Console formatter (TDD)

**Files:**
- Create: `src/Harvester/Formatters/IncidentChangeFormatter.cs`
- Create: `tests/UnitTests/Formatters/IncidentChangeFormatterTests.cs`
- Modify: `src/Harvester/GlobalUsings.cs` and `tests/UnitTests/GlobalUsings.cs` — un-comment the `Formatters` global using in both.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace HoustonIncidents.Harvester.UnitTests.Formatters;

/// <summary>
/// Tests for <see cref="IncidentChangeFormatter"/>.
/// </summary>
public sealed class IncidentChangeFormatterTests
{
    private static readonly Incident Sample = new("FD", "AUTUMN FOREST DR", "EMS EVENT", "11/24/2026 09:33");

    private static readonly DateTimeOffset Timestamp = new(2026, 11, 24, 9, 33, 12, TimeSpan.Zero);

    [Fact]
    public void Format_NewChange_MatchesSpecLine()
    {
        var line = IncidentChangeFormatter.Format(new IncidentChange(IncidentChangeKind.New, Sample), Timestamp);

        Assert.Equal("[09:33:12] NEW       FD | AUTUMN FOREST DR | EMS EVENT | 11/24/2026 09:33", line);
    }

    [Fact]
    public void Format_ClosedChange_MatchesSpecLine()
    {
        var line = IncidentChangeFormatter.Format(new IncidentChange(IncidentChangeKind.Closed, Sample), Timestamp);

        Assert.Equal("[09:33:12] CLOSED    FD | AUTUMN FOREST DR | EMS EVENT | 11/24/2026 09:33", line);
    }
}
```

- [ ] **Step 2: Un-comment both `Formatters` global usings, then run to verify failure**

Run: `dotnet test tests/UnitTests/HoustonIncidents.Harvester.UnitTests.csproj`
Expected: build FAIL — `IncidentChangeFormatter` / namespace `Formatters` not found.

- [ ] **Step 3: Implement `Formatters/IncidentChangeFormatter.cs`**

```csharp
namespace HoustonIncidents.Harvester.Formatters;

/// <summary>
/// Renders incident changes as single console lines.
/// </summary>
internal static class IncidentChangeFormatter
{
    /// <summary>Label printed for a new incident.</summary>
    private const string NewLabel = "NEW";

    /// <summary>Label printed for a closed incident.</summary>
    private const string ClosedLabel = "CLOSED";

    /// <summary>
    /// Formats a change as <c>[HH:mm:ss] LABEL     Agency | Address | Type | Opened</c>.
    /// </summary>
    /// <param name="change">The change to format.</param>
    /// <param name="timestamp">The time the change was detected.</param>
    /// <returns>The formatted line.</returns>
    public static string Format(IncidentChange change, DateTimeOffset timestamp)
    {
        var label = change.Kind == IncidentChangeKind.New ? NewLabel : ClosedLabel;

        var incident = change.Incident;

        return $"[{timestamp:HH:mm:ss}] {label,-9} {incident.Agency} | {incident.Address} | {incident.IncidentType} | {incident.CallTimeOpened}";
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/UnitTests/HoustonIncidents.Harvester.UnitTests.csproj`
Expected: 15 passed.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: format incident changes for console" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Options and page client

**Files:**
- Create: `src/Harvester/Options/IncidentOptions.cs`, `src/Harvester/Interfaces/IIncidentPageClient.cs`, `src/Harvester/Services/IncidentPageClient.cs`
- Modify: `src/Harvester/GlobalUsings.cs` — un-comment the `Options` global using.

The client is a thin `HttpClient` wrapper with no logic worth unit-testing; it is verified end-to-end in Task 8.

- [ ] **Step 1: Create `Options/IncidentOptions.cs`**

```csharp
namespace HoustonIncidents.Harvester.Options;

/// <summary>
/// Settings for the incident harvester, bound from the <c>Incidents</c> configuration section
/// (override with environment variables such as <c>Incidents__PollIntervalSeconds</c>).
/// </summary>
internal sealed class IncidentOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Incidents";

    /// <summary>Gets or sets the page URL.</summary>
    public string Url { get; set; } = "https://cohweb.houstontx.gov/ActiveIncidents/Combined.aspx?agency=%25";

    /// <summary>Gets or sets the seconds between polls.</summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>Gets or sets the per-request timeout in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 20;

    /// <summary>Gets or sets the User-Agent header sent with each request.</summary>
    public string UserAgent { get; set; } = "Mozilla/5.0 (compatible; HoustonIncidentsHarvester/1.0)";
}
```

- [ ] **Step 2: Create `Interfaces/IIncidentPageClient.cs`**

```csharp
namespace HoustonIncidents.Harvester.Interfaces;

/// <summary>
/// Downloads the Active Incidents page.
/// </summary>
internal interface IIncidentPageClient
{
    /// <summary>
    /// Fetches the page HTML.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The HTML, or a failure describing an HTTP error or timeout.</returns>
    Task<Result<string>> GetPageAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Create `Services/IncidentPageClient.cs`**

```csharp
namespace HoustonIncidents.Harvester.Services;

/// <summary>
/// <see cref="HttpClient"/>-based implementation of <see cref="IIncidentPageClient"/>.
/// </summary>
/// <param name="httpClient">The configured HTTP client (timeout and User-Agent set at registration).</param>
/// <param name="options">The harvester options supplying the page URL.</param>
internal sealed class IncidentPageClient(HttpClient httpClient, IOptions<IncidentOptions> options) : IIncidentPageClient
{
    /// <inheritdoc />
    public async Task<Result<string>> GetPageAsync(CancellationToken cancellationToken)
    {
        try
        {
            var html = await httpClient.GetStringAsync(options.Value.Url, cancellationToken).ConfigureAwait(false);

            return Result<string>.Ok(html);
        }
        catch (HttpRequestException ex)
        {
            return Result<string>.Fail($"HTTP request failed: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<string>.Fail("HTTP request timed out.");
        }
    }
}
```

- [ ] **Step 4: Commit** (build still pending the `Workers` namespace in Task 7)

```bash
git add -A
git commit -m "feat: add options and HTTP page client" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Worker, DI registration, and Program

**Files:**
- Create: `src/Harvester/Workers/IncidentWorker.cs`, `src/Harvester/ServiceRegistryExtensions.cs`
- Modify: `src/Harvester/Program.cs` (replace the stub)
- Modify: `src/Harvester/GlobalUsings.cs` — un-comment the `Workers` global using (all global usings are now live).

- [ ] **Step 1: Create `Workers/IncidentWorker.cs`**

```csharp
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
```

- [ ] **Step 2: Create `ServiceRegistryExtensions.cs`**

```csharp
namespace HoustonIncidents.Harvester;

/// <summary>
/// Dependency-injection registration for the harvester.
/// </summary>
internal static class ServiceRegistryExtensions
{
    /// <summary>
    /// Registers the harvester's options, HTTP client, services, and hosted worker.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddIncidentServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IncidentOptions>(configuration.GetSection(IncidentOptions.SectionName));

        services.AddHttpClient<IIncidentPageClient, IncidentPageClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<IncidentOptions>>().Value;

            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);

            client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
        });

        services.TryAddSingleton<IIncidentTableParser, IncidentTableParser>();

        services.TryAddSingleton<IIncidentTracker, IncidentTracker>();

        services.AddHostedService<IncidentWorker>();

        return services;
    }
}
```

- [ ] **Step 3: Replace `Program.cs`**

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddIncidentServices(builder.Configuration);

using var host = builder.Build();

await host.RunAsync().ConfigureAwait(false);
```

- [ ] **Step 4: Build everything and run all tests**

Run: `dotnet build HoustonIncidents.slnx && dotnet test tests/UnitTests/HoustonIncidents.Harvester.UnitTests.csproj`
Expected: Build succeeded with 0 warnings / 0 errors; 15 tests passed. Fix any warning (they are errors) before continuing.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add worker, DI registration, and entry point" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Live verification

**Files:** none changed (unless a defect is found).

- [ ] **Step 1: Count the rows the live page really has**

```bash
curl -s -A "Mozilla/5.0" "https://cohweb.houstontx.gov/ActiveIncidents/Combined.aspx?agency=%25" | grep -c '<td style="width:50px;">FD</td>\|<td style="width:50px;">PD</td>'
```

Note the number (call it N). Row labels other than FD/PD may exist; if the harvester count differs, inspect the page for the agency codes actually used.

- [ ] **Step 2: Run the harvester with a short poll interval**

```bash
cd /d/source/repos/HoustonIncidents
Incidents__PollIntervalSeconds=15 dotnet run --project src/Harvester -c Release
```

Expected within the first seconds: a `Harvester started; polling every 15s` log line, then roughly N lines like `[HH:mm:ss] NEW       FD | ADDRESS | TYPE | 11/24/2026 HH:mm`. Let it run 2-3 minutes: later polls should print only occasional `NEW` and `CLOSED` lines (a busy city changes every few minutes), not the whole list again. Stop with Ctrl+C; expect `Harvester stopping`.

- [ ] **Step 3: Verify the failure path**

```bash
Incidents__Url=https://cohweb.houstontx.gov/ActiveIncidents/DoesNotExist.aspx Incidents__PollIntervalSeconds=15 dotnet run --project src/Harvester -c Release
```

Expected: a `Poll skipped: HTTP request failed: ...` warning (or `Table 'GridView2' not found`), no `CLOSED` lines, no crash. Ctrl+C to stop.

- [ ] **Step 4: If anything in steps 2-3 misbehaves, stop and debug** (use superpowers:systematic-debugging); otherwise commit nothing further.

---

## Self-Review

**Spec coverage:** four-field `Incident` + identity (Task 2); GridView2-only parsing, header-based columns, entity decoding, failure when table missing (Task 3); baseline-as-NEW, NEW/CLOSED diffing, duplicate rows, re-open (Task 4); console line format with exact spacing (Task 5); configurable 30s poll, URL, timeout, `IOptions<T>` (Task 6); failed poll logged and skipped, never wipes tracked set; `AddIncidentServices()` registration; `BackgroundService` + `PeriodicTimer` (Task 7); parser/tracker/formatter tests and live check (Tasks 3-5, 8). No gaps.

**Placeholder scan:** none; every code step has full code. The commented-out global usings in Tasks 3-6 are a deliberate sequencing device, restored in Tasks 5-7.

**Type consistency:** `Incident(Agency, Address, IncidentType, CallTimeOpened)`, `IncidentChange(Kind, Incident)`, `IncidentChangeKind.New/Closed`, `Result<T>.Ok/Fail/IsSuccess/Value/Error`, `IIncidentTableParser.Parse`, `IIncidentTracker.Apply`, `IIncidentPageClient.GetPageAsync`, `IncidentChangeFormatter.Format(change, timestamp)` — names and signatures match across all tasks and tests.
