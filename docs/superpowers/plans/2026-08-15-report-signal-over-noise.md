# Report Signal-over-Noise Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the dead-notes worklist in `kbo report` short and true by teaching it three distinctions the 2026-08-14 report proved it lacks: registry exclusions (archives are not knowledge), lifecycle artifacts (executed plans/specs die on completion, not by read-counts), and dormant sources (a paused project's docs are not dead knowledge).

**Architecture:** Three small, independent mechanisms, all pure and testable: (1) an `exclude` list on glob registry sources, applied during glob expansion; (2) a `NoteRole` path classifier (mirror of the existing `ContentKind` pattern) that keeps lifecycle artifacts out of the dead worklist; (3) a per-source last-activity query over silver that withholds dead notes from sources with no recent events, reporting them as *dormant* instead. `GoldReport` grows three fields; the renderer grows two short sections. No event-schema changes, no silver-schema changes.

**Tech Stack:** C# / .NET 10, xUnit 2.9.3, DuckDB.NET (silver queries), YamlDotNet (registry).

**Spec:** No separate spec file — the Context section below is the authoritative statement of intent; argue from it. Evidence: `~/Knowledge/_generated/kbo-report.md` generated 2026-08-14T20:28:31Z.

## Context (what the 2026-08-14 report showed)

The dead-notes worklist had ~155 rows, of which:
- 69 inventory notes came from `repo-kb-observability-private-archive` — an **archive** swept in by the `Repository/*/docs` glob. Archives are not live knowledge; they don't belong in inventory at all.
- ~50 rows were executed `docs/superpowers/plans/*` and `specs/*` documents plus journal files. These are **lifecycle artifacts**: they are *done* when their work is done. A read-count death condition is the wrong one for them; they would sit on the worklist forever.
- ~70 rows came from `repo-CareerPlatform`, whose last recorded activity is 2026-07-15 (`docs/backlog.md`, 24 reads, then silence). The project is **dormant**; its docs are not dead knowledge, they are paused with the project and will revive with it.

After this plan, the dead worklist contains only *reference notes in active sources with zero reads* — the category that actually deserves ritual attention. Everything withheld is still visible (counts + dormant section), never silently dropped (no-silent-caps).

A fourth noise source surfaced on 2026-08-15 while inspecting the live dashboard: the dead-man panel showed `report` and `audit` red at 3.2–3.3 days silent, yet both are `JobCadence.Weekly` jobs (`PulseCommand.cs`) that are *due* only after 6.5 days — pulse correctly said "not due". The panel's flat `DeadManThresholdDays = 3` guarantees every weekly job burns red from day 3 until its next run: a structural false positive that trains the owner to ignore red, which is the one failure mode a dead-man panel must not have. Task 6 makes the threshold cadence-aware.

## Global Constraints

- Git commits: plain messages, **no AI co-author trailers, no generated-with footers** (owner's global instruction — overrides any default).
- TDD: every behavior change lands with its failing test first; run `dotnet test tests/Kbo.Tests` after each step that says so.
- P2: renderers render, they never compute. All new logic lives in `GoldComputer` / `Registry`, never in `MarkdownRenderer`.
- P3: silver stays disposable — this plan makes **no** silver schema changes; new queries read existing `events_preferred` columns only.
- No-silent-caps: anything excluded from a worklist must appear in the report as a count or a section with a reason.
- The live registry file is `~/.config/kbo/registry.yaml` (machine config, not in this repo). It is edited in Task 1 only, additively.
- Thresholds are named constants on `GoldComputer`, same style as the existing `ReadWindowDays = 60`.

## File Structure

| File | Responsibility |
|---|---|
| `src/Kbo/Registry/KnowledgeRegistry.cs` (modify) | Parse per-source `exclude:` list; skip excluded matches during glob expansion |
| `src/Kbo/Gold/NoteRole.cs` (create) | Pure path→role classifier: `reference` vs `lifecycle` |
| `src/Kbo/Gold/GoldComputer.cs` (modify) | Lifecycle exclusion from dead check; per-source activity query; dormant partition |
| `src/Kbo/Gold/GoldReport.cs` (modify) | New fields: `DormantAfterDays`, `LifecycleCounts`, `DormantSources`; new record `DormantSource` |
| `src/Kbo/Gold/MarkdownRenderer.cs` (modify) | Two new sections: lifecycle counts, dormant sources |
| `src/Kbo/Cli/ReportCommand.cs` (modify) | Summary line includes lifecycle/dormant counts |
| `tests/Kbo.Tests/RegistryGlobTests.cs` (modify) | Exclude-list tests |
| `tests/Kbo.Tests/NoteRoleTests.cs` (create) | Classifier tests |
| `tests/Kbo.Tests/GoldComputerTests.cs` (modify) | Lifecycle + dormancy behavior tests |
| `tests/Kbo.Tests/MarkdownRendererTests.cs` (modify) | New-section rendering tests; constructor-arg updates |
| `docs/adr/` (create next-numbered ADR) | Decision record: type-aware death + dormancy |
| `CHANGELOG.md` (modify) | One entry |
| `src/Kbo/Jobs/DeadMan.cs` (create) | Per-cadence dead-man thresholds (Task 6) |
| `src/Kbo/Cli/PulseCommand.cs` (modify) | Authoritative `JobCadences` name→cadence map (Task 6) |
| `src/Kbo/Gold/DashboardComputer.cs` / `DashboardGold.cs` / `DashboardRenderer.cs` (modify) | Cadence-aware tile status (Task 6) |

---

### Task 1: Registry `exclude` list for glob sources

**Files:**
- Modify: `src/Kbo/Registry/KnowledgeRegistry.cs`
- Test: `tests/Kbo.Tests/RegistryGlobTests.cs`
- Modify (machine config, last step): `~/.config/kbo/registry.yaml`

**Interfaces:**
- Consumes: existing `KnowledgeRegistry.Parse(string yaml, string?)`, `ExpandGlob(...)`.
- Produces: registry YAML accepts optional `exclude: [name, ...]` on a source whose `root` contains `*`; expanded sources whose `*`-matched segment equals an excluded name are skipped. `exclude` on a non-glob source is a `RegistryFormatException` validation error. No public API signature changes.

- [ ] **Step 1: Write the failing tests** (append to `RegistryGlobTests.cs`, following its existing temp-dir style; if the file builds fixtures differently, keep its local helpers and only take the assertions below)

```csharp
[Fact]
public void Glob_expansion_skips_excluded_directory_names()
{
    string workspace = Directory.CreateTempSubdirectory("kbo-glob-exclude").FullName;
    try
    {
        Directory.CreateDirectory(Path.Combine(workspace, "Alpha", "docs"));
        Directory.CreateDirectory(Path.Combine(workspace, "Beta", "docs"));
        Directory.CreateDirectory(Path.Combine(workspace, "kb-observability-private-archive", "docs"));

        KnowledgeRegistry registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: repo
                layer: local
                root: {workspace}/*/docs
                exclude: [kb-observability-private-archive]
            """);

        string[] ids = registry.Sources.Select(source => source.Id).ToArray();
        Assert.Contains("repo-Alpha", ids);
        Assert.Contains("repo-Beta", ids);
        Assert.DoesNotContain("repo-kb-observability-private-archive", ids);
    }
    finally
    {
        Directory.Delete(workspace, recursive: true);
    }
}

[Fact]
public void Exclude_on_non_glob_source_is_a_validation_error()
{
    RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() =>
        KnowledgeRegistry.Parse("""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: /tmp/vault
                exclude: [something]
            """));
    Assert.Contains("'exclude' requires a glob root", exception.Message);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Kbo.Tests --filter "FullyQualifiedName~RegistryGlobTests" -v minimal`
Expected: FAIL — YAML deserializer ignores unknown `exclude` today, so the first test fails on `DoesNotContain`; the second fails because no error is raised.

- [ ] **Step 3: Implement**

In `KnowledgeRegistry.cs`:

1. Add to `SourceEntry` (private nested class, bottom of file):
```csharp
public List<string>? Exclude { get; set; }
```
2. In `Parse`, inside the source loop, after the `IsPathRooted` check and before the glob branch, validate:
```csharp
bool hasExclude = entry.Exclude is { Count: > 0 };
if (hasExclude && !entry.Root!.Contains('*'))
{
    errors.Add($"source '{entry.Id}': 'exclude' requires a glob root");
    continue;
}
```
3. Change `ExpandGlob` signature to accept the list and skip matches:
```csharp
private static string? ExpandGlob(string id, KnowledgeLayer layer, string root,
    IReadOnlyCollection<string> exclude,
    List<KnowledgeSource> sources, HashSet<string> seenIds)
```
Pass `entry.Exclude ?? (IReadOnlyCollection<string>)Array.Empty<string>()` at the call site. In the final `foreach` over candidates, before adding:
```csharp
if (matched.Any(exclude.Contains))
{
    continue;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Kbo.Tests --filter "FullyQualifiedName~Registry" -v minimal`
Expected: PASS, including all pre-existing registry tests.

- [ ] **Step 5: Commit**

```bash
git add src/Kbo/Registry/KnowledgeRegistry.cs tests/Kbo.Tests/RegistryGlobTests.cs
git commit -m "feat(registry): exclude list for glob sources"
```

- [ ] **Step 6: Update the live machine registry** (additive edit; owner-approved by this plan)

Edit `~/.config/kbo/registry.yaml` — change only the `repo` entry:
```yaml
  - id: repo
    layer: local
    root: /home/admin/Repository/*/docs
    exclude: [kb-observability-private-archive]
```
Verify: `kbo registry` (or `kbo registry validate` if that is the subcommand form — run `kbo registry --help` first) exits 0 and its output no longer lists `repo-kb-observability-private-archive`. Note: the installed binary predates this feature — full effect is only verifiable after Task 5 redeploys it; at this point only YAML well-formedness is checked.

---

### Task 2: `NoteRole` classifier (lifecycle vs reference)

**Files:**
- Create: `src/Kbo/Gold/NoteRole.cs`
- Test: create `tests/Kbo.Tests/NoteRoleTests.cs`

**Interfaces:**
- Consumes: nothing (pure).
- Produces: `NoteRole.Of(string path)` → `"reference" | "lifecycle"`; constants `NoteRole.Reference`, `NoteRole.Lifecycle`. Task 3 depends on exactly these names.

- [ ] **Step 1: Write the failing tests**

```csharp
using Kbo.Gold;

namespace Kbo.Tests;

public class NoteRoleTests
{
    [Theory]
    [InlineData("/r/CareerPlatform/docs/superpowers/plans/2026-06-27-kanban-a.md")]
    [InlineData("/r/CareerPlatform/docs/superpowers/specs/career-page.md")]
    [InlineData("/r/kbo/docs/journal/2026-08-11.md")]
    public void Executed_plans_specs_and_journals_are_lifecycle(string path)
    {
        Assert.Equal(NoteRole.Lifecycle, NoteRole.Of(path));
    }

    [Theory]
    [InlineData("/home/admin/Knowledge/homelab-sec/Glossary/Beacon.md")]
    [InlineData("/r/CareerPlatform/docs/okf/tenancy/tenant-resolution.md")]
    [InlineData("/r/CareerPlatform/docs/adr/0001-record-architecture-decisions.md")]
    [InlineData("/r/X/docs/planshet.md")]
    public void Reference_notes_including_adrs_are_reference(string path)
    {
        Assert.Equal(NoteRole.Reference, NoteRole.Of(path));
    }
}
```
(Note the `planshet.md` case: matching must be on whole path segments — `/journal/`, `/superpowers/plans/`, `/superpowers/specs/` — not on bare substrings of file names. ADRs stay `reference` deliberately: they are looked up, not executed.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Kbo.Tests --filter "FullyQualifiedName~NoteRoleTests" -v minimal`
Expected: FAIL — `NoteRole` does not exist (compile error is the failure here; that's fine, proceed).

- [ ] **Step 3: Implement**

```csharp
namespace Kbo.Gold;

/// <summary>
/// Classifies a note by its death condition: reference notes die by
/// non-use and belong on the dead worklist; lifecycle artifacts
/// (executed plans/specs, dated journals) are done when their work is
/// done and never belong there. Path-segment-based; pure, no I/O.
/// Mirror of ContentKind (ADR-0025 pattern).
/// </summary>
public static class NoteRole
{
    public const string Reference = "reference";
    public const string Lifecycle = "lifecycle";

    private static readonly string[] LifecycleSegments =
    [
        "/superpowers/plans/",
        "/superpowers/specs/",
        "/journal/",
    ];

    public static string Of(string path)
    {
        string normalized = path.Replace('\\', '/');
        return LifecycleSegments.Any(segment => normalized.Contains(segment, StringComparison.Ordinal))
            ? Lifecycle
            : Reference;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Kbo.Tests --filter "FullyQualifiedName~NoteRoleTests" -v minimal`
Expected: PASS (7 test cases).

- [ ] **Step 5: Commit**

```bash
git add src/Kbo/Gold/NoteRole.cs tests/Kbo.Tests/NoteRoleTests.cs
git commit -m "feat(gold): NoteRole classifier for type-aware death conditions"
```

---

### Task 3: Exclude lifecycle artifacts from the dead worklist

**Files:**
- Modify: `src/Kbo/Gold/GoldComputer.cs` (dead-note loop, ~lines 31–49)
- Modify: `src/Kbo/Gold/GoldReport.cs` (record fields)
- Modify: `src/Kbo/Gold/MarkdownRenderer.cs` (new section after Inventory)
- Modify: `src/Kbo/Cli/ReportCommand.cs` (summary line ~91)
- Test: `tests/Kbo.Tests/GoldComputerTests.cs`, `tests/Kbo.Tests/MarkdownRendererTests.cs`

**Interfaces:**
- Consumes: `NoteRole.Of(path)` from Task 2.
- Produces: `GoldReport` gains `IReadOnlyDictionary<string, int> LifecycleCounts` (sourceId → count of lifecycle notes in inventory), inserted **after** `InventoryCounts`. Lifecycle notes never appear in `DeadNotes` (they remain eligible for `HotNotes`/`StaleNotes` — being read is fine). Task 4 builds on this constructor shape; the full final constructor is spelled out in Task 4 Step 3.

- [ ] **Step 1: Write the failing test** (append to `GoldComputerTests`; the fixture's `Note(relativePath, modifiedDaysAgo)` helper and `Compute(params JsonObject[] events)` already exist)

```csharp
[Fact]
public void Lifecycle_notes_are_counted_but_never_dead()
{
    Note("Glossary/beacon.md", modifiedDaysAgo: 40);                       // reference, unread → dead
    Note("docs/superpowers/plans/2026-06-01-old-plan.md", modifiedDaysAgo: 40); // lifecycle, unread → not dead

    GoldReport report = Compute();

    Assert.Single(report.DeadNotes);
    Assert.EndsWith("Glossary/beacon.md", report.DeadNotes[0].Path);
    Assert.Equal(1, report.LifecycleCounts["vault"]);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Kbo.Tests --filter "FullyQualifiedName~Lifecycle_notes" -v minimal`
Expected: FAIL — `GoldReport` has no `LifecycleCounts` (compile error).

- [ ] **Step 3: Implement**

In `GoldReport.cs`, add the field after `InventoryCounts`:
```csharp
    IReadOnlyDictionary<string, int> InventoryCounts,
    IReadOnlyDictionary<string, int> LifecycleCounts,
```
In `GoldComputer.Compute`, before the dead/stale loop:
```csharp
Dictionary<string, int> lifecycleCounts = new();
```
Inside the `foreach (InventoryNote note in inventory)` loop, as the first statements:
```csharp
bool isLifecycle = NoteRole.Of(note.Path) == NoteRole.Lifecycle;
if (isLifecycle)
{
    lifecycleCounts[note.SourceId] = lifecycleCounts.GetValueOrDefault(note.SourceId) + 1;
}
```
and guard the dead check only (stale check untouched):
```csharp
if (!isLifecycle && daysSinceModified >= MinInventoryAgeDays && readsInWindow == 0)
```
Pass `lifecycleCounts` in the `GoldReport` constructor call. Fix every other `new GoldReport(...)` call site the compiler reports (renderer tests, `ReportCommandTests`) by inserting an empty dictionary or the computed value — mechanical, guided by compile errors.

In `MarkdownRenderer.Render`, after the Inventory block:
```csharp
markdown.AppendLine("## Lifecycle artifacts — die on completion, excluded from the dead worklist");
markdown.AppendLine();
if (report.LifecycleCounts.Count == 0)
{
    markdown.AppendLine("none");
}
else
{
    foreach (KeyValuePair<string, int> entry in report.LifecycleCounts.OrderBy(e => e.Key, StringComparer.Ordinal))
    {
        markdown.AppendLine(CultureInfo.InvariantCulture, $"- `{entry.Key}`: {entry.Value} note(s) (plans / specs / journal)");
    }
}
markdown.AppendLine();
```
In `ReportCommand.cs` line ~91, extend the summary interpolation with:
```csharp
$"... {report.LifecycleCounts.Values.Sum()} lifecycle excluded ..."
```
(keep the existing dead/hot/stale wording intact, append the new clause).

- [ ] **Step 4: Add renderer test and run the full suite**

Append to `MarkdownRendererTests.cs` (reuse its existing report-construction helper, adding the new argument):
```csharp
[Fact]
public void Renders_lifecycle_counts_section()
{
    // construct a GoldReport with LifecycleCounts = { ["repo-CareerPlatform"] = 46 }
    // via the file's existing builder/helper, then:
    Assert.Contains("## Lifecycle artifacts", markdown);
    Assert.Contains("`repo-CareerPlatform`: 46 note(s)", markdown);
}
```
Run: `dotnet test tests/Kbo.Tests -v minimal`
Expected: PASS — full suite green (constructor call sites all fixed).

- [ ] **Step 5: Commit**

```bash
git add src/Kbo/Gold/ src/Kbo/Cli/ReportCommand.cs tests/Kbo.Tests/
git commit -m "feat(gold): lifecycle artifacts excluded from dead worklist, counted in report"
```

---

### Task 4: Dormant sources — withhold dead notes from inactive projects

**Files:**
- Modify: `src/Kbo/Gold/GoldComputer.cs`
- Modify: `src/Kbo/Gold/GoldReport.cs`
- Modify: `src/Kbo/Gold/MarkdownRenderer.cs`
- Modify: `src/Kbo/Cli/ReportCommand.cs` (summary line)
- Test: `tests/Kbo.Tests/GoldComputerTests.cs`, `tests/Kbo.Tests/MarkdownRendererTests.cs`

**Interfaces:**
- Consumes: `events_preferred` view (existing columns: `type`, `time`, `subject`, `repo`); `KnowledgeRegistry.Resolve(path)`.
- Produces:
```csharp
public sealed record DormantSource(string SourceId, DateTimeOffset? LastActivity, int WithheldDeadNotes);
```
`GoldComputer.DormantAfterDays = 21`. A source is **dormant** when its last activity is older than 21 days (or it has none). Activity of a source = the newest event where either `registry.Resolve(subject) == sourceId`, or the source root sits inside the event's `repo` (`source.Root == repo || source.Root.StartsWith(repo + "/")`). Dead notes from dormant sources go into `DormantSource.WithheldDeadNotes` counts instead of `DeadNotes`. Final `GoldReport` constructor:
```csharp
public sealed record GoldReport(
    DateTimeOffset GeneratedAt,
    string Machine,
    int MinInventoryAgeDays,
    int ReadWindowDays,
    int StaleMinReads,
    int StaleUnmodifiedDays,
    int DormantAfterDays,
    IReadOnlyDictionary<string, int> InventoryCounts,
    IReadOnlyDictionary<string, int> LifecycleCounts,
    IReadOnlyList<DormantSource> DormantSources,
    IReadOnlyList<DeadNote> DeadNotes,
    IReadOnlyList<HotNote> HotNotes,
    IReadOnlyList<StaleNote> StaleNotes);
```

- [ ] **Step 1: Write the failing tests** (append to `GoldComputerTests`; note the fixture's registry has sources `vault` and `skills` — the events below use `subject` paths inside `vaultRoot` to drive activity)

```csharp
[Fact]
public void Source_with_recent_activity_keeps_its_dead_notes_on_the_worklist()
{
    string deadPath = Note("Glossary/unread.md", modifiedDaysAgo: 40);
    string readPath = Note("Now.md", modifiedDaysAgo: 5);

    GoldReport report = Compute(ReadEvent("01TESTACTIVE00000000000001", readPath, daysAgo: 2));

    Assert.Contains(report.DeadNotes, note => note.Path == deadPath);
    Assert.DoesNotContain(report.DormantSources, source => source.SourceId == "vault");
}

[Fact]
public void Source_silent_beyond_threshold_is_dormant_and_dead_notes_are_withheld()
{
    string deadPath = Note("Glossary/unread.md", modifiedDaysAgo: 40);
    string oldReadPath = Note("Now.md", modifiedDaysAgo: 40);

    GoldReport report = Compute(ReadEvent("01TESTDORMANT0000000000001", oldReadPath, daysAgo: 30));

    Assert.DoesNotContain(report.DeadNotes, note => note.Path == deadPath);
    DormantSource dormant = Assert.Single(report.DormantSources, source => source.SourceId == "vault");
    Assert.Equal(1, dormant.WithheldDeadNotes);
    Assert.NotNull(dormant.LastActivity);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Kbo.Tests --filter "FullyQualifiedName~dormant" -v minimal`
Expected: FAIL — `DormantSources` does not exist (compile error).

- [ ] **Step 3: Implement**

In `GoldComputer.cs` add the constant and the query:
```csharp
public const int DormantAfterDays = 21;

private static Dictionary<string, DateTimeOffset> QuerySourceActivity(
    string silverPath, KnowledgeRegistry registry)
{
    Dictionary<string, DateTimeOffset> lastBySource = new();
    void Bump(string sourceId, DateTimeOffset time)
    {
        if (!lastBySource.TryGetValue(sourceId, out DateTimeOffset existing) || time > existing)
        {
            lastBySource[sourceId] = time;
        }
    }

    using DuckDBConnection connection = SilverConnection.OpenReadOnly(silverPath);

    using (DuckDBCommand bySubject = connection.CreateCommand())
    {
        bySubject.CommandText = """
            SELECT subject, max(time) FROM events_preferred
            WHERE subject IS NOT NULL GROUP BY subject
            """;
        using DuckDBDataReader reader = (DuckDBDataReader)bySubject.ExecuteReader();
        while (reader.Read())
        {
            string? sourceId = registry.Resolve(reader.GetString(0));
            if (sourceId is not null)
            {
                Bump(sourceId, new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc)));
            }
        }
    }

    using (DuckDBCommand byRepo = connection.CreateCommand())
    {
        byRepo.CommandText = """
            SELECT repo, max(time) FROM events_preferred
            WHERE repo IS NOT NULL GROUP BY repo
            """;
        using DuckDBDataReader reader = (DuckDBDataReader)byRepo.ExecuteReader();
        while (reader.Read())
        {
            string repo = reader.GetString(0);
            DateTimeOffset time = new(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));
            foreach (KnowledgeSource source in registry.Sources)
            {
                if (source.Root == repo || source.Root.StartsWith(repo + "/", StringComparison.Ordinal))
                {
                    Bump(source.Id, time);
                }
            }
        }
    }

    return lastBySource;
}
```
In `Compute`, after building `deadNotes` (and before constructing the report):
```csharp
Dictionary<string, DateTimeOffset> activityBySource = QuerySourceActivity(silverPath, registry);
DateTimeOffset dormantCutoff = now.AddDays(-DormantAfterDays);
HashSet<string> dormantSourceIds = inventoryCounts.Keys
    .Where(id => !activityBySource.TryGetValue(id, out DateTimeOffset last) || last < dormantCutoff)
    .ToHashSet();

List<DormantSource> dormantSources = dormantSourceIds
    .OrderBy(id => id, StringComparer.Ordinal)
    .Select(id => new DormantSource(
        id,
        activityBySource.TryGetValue(id, out DateTimeOffset last) ? last : null,
        deadNotes.Count(note => note.SourceId == id)))
    .ToList();

deadNotes = deadNotes.Where(note => !dormantSourceIds.Contains(note.SourceId)).ToList();
```
Add `DormantSource` record to `GoldReport.cs`, extend the constructor per the Interfaces block above (insert `DormantAfterDays` after `StaleUnmodifiedDays`, `DormantSources` after `LifecycleCounts`), pass `DormantAfterDays` and `dormantSources` in `Compute`, and fix all other constructor call sites the compiler reports.

In `MarkdownRenderer.Render`, immediately **before** the Dead-notes section:
```csharp
markdown.AppendLine(CultureInfo.InvariantCulture, $"## Dormant sources — no activity in {report.DormantAfterDays}d, dead-note check suspended");
markdown.AppendLine();
if (report.DormantSources.Count == 0)
{
    markdown.AppendLine("none");
}
else
{
    markdown.AppendLine("| Source | Last activity | Dead notes withheld |");
    markdown.AppendLine("|--------|---------------|--------------------:|");
    foreach (DormantSource source in report.DormantSources)
    {
        string lastActivity = source.LastActivity is null ? "never" : Timestamp(source.LastActivity.Value);
        markdown.AppendLine(CultureInfo.InvariantCulture,
            $"| `{source.SourceId}` | {lastActivity} | {source.WithheldDeadNotes} |");
    }
}
markdown.AppendLine();
```
Extend the `ReportCommand` summary with `{report.DormantSources.Count} dormant source(s)`.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test tests/Kbo.Tests -v minimal`
Expected: PASS. If any pre-existing `GoldComputerTests` case now trips dormancy (a fixture source with no events at all becomes dormant), that test's fixture must gain one recent `ReadEvent(...)` to assert its original intent explicitly — adjust the fixture, never weaken the assertion.

- [ ] **Step 5: Add renderer test and run it**

```csharp
[Fact]
public void Renders_dormant_sources_section()
{
    // construct a GoldReport with one DormantSource("repo-CareerPlatform", <date>, 70)
    // via the file's existing builder/helper, then:
    Assert.Contains("## Dormant sources", markdown);
    Assert.Contains("`repo-CareerPlatform`", markdown);
    Assert.Contains("| 70 |", markdown);
}
```
Run: `dotnet test tests/Kbo.Tests --filter "FullyQualifiedName~MarkdownRenderer" -v minimal`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Kbo/Gold/ src/Kbo/Cli/ReportCommand.cs tests/Kbo.Tests/
git commit -m "feat(gold): dormant sources withhold dead notes from the worklist"
```

---

### Task 5: Documentation, deploy, real-data verification

**Files:**
- Create: `docs/adr/<next-number>-type-aware-death-and-dormancy.md`
- Modify: `docs/layer-gold.md` (dead-notes semantics section), `CHANGELOG.md`
- Deploy: `~/.local/bin/kbo`

**Interfaces:**
- Consumes: everything above, plus the live bronze/silver data.
- Produces: a regenerated `~/Knowledge/_generated/kbo-report.md` whose dead worklist contains only reference notes from active sources.

- [ ] **Step 1: Write the ADR**

Run `ls docs/adr/` to find the next free number `NNNN`, then create `docs/adr/NNNN-type-aware-death-and-dormancy.md`:
```markdown
# NNNN — Type-aware death conditions and dormant sources

Status: accepted · Date: 2026-08-15

## Context
The 2026-08-14 report listed ~155 dead notes; ~120 were noise: an archived
repo swept in by the registry glob, executed superpowers plans/specs and
journals (lifecycle artifacts), and docs of a project with no activity
since 2026-07-15.

## Decision
1. Registry glob sources accept `exclude: [dirname, ...]`.
2. A note's death condition depends on its role (NoteRole): reference
   notes die by non-use; lifecycle artifacts (`/superpowers/plans/`,
   `/superpowers/specs/`, `/journal/` path segments) never enter the dead
   worklist. ADRs stay reference: they are looked up, not executed.
3. Sources with no activity for 21 days are dormant; their dead notes are
   withheld and reported as a count with last-activity date (no-silent-caps).

## Consequences
The dead worklist shrinks to genuine anomalies. Withheld categories stay
visible as counts/sections. Roles are path-based for now; per-note
frontmatter death conditions are a possible future extension, deliberately
out of scope (YAGNI until a ritual asks for it).
```

- [ ] **Step 2: Update `docs/layer-gold.md` and `CHANGELOG.md`**

In `layer-gold.md`, find the dead-notes definition and amend it to state the three filters (lifecycle exclusion, dormancy withholding, registry excludes) with one sentence each, referencing the ADR by number. In `CHANGELOG.md`, add under a new `## 2026-08-15` heading (or the file's existing convention — mirror it):
```markdown
- report: dead worklist is now type- and activity-aware — lifecycle
  artifacts (plans/specs/journals) and dormant sources are excluded and
  reported separately; registry glob sources support `exclude:`.
```

- [ ] **Step 3: Commit docs**

```bash
git add docs/adr/ docs/layer-gold.md CHANGELOG.md
git commit -m "docs: ADR + gold-layer docs for type-aware death and dormancy"
```

- [ ] **Step 4: Build and deploy the binary**

```bash
dotnet publish src/Kbo -c Release -r linux-x64 --self-contained true -o /tmp/kbo-publish
install -m 755 /tmp/kbo-publish/Kbo ~/.local/bin/kbo
kbo doctor
```
Expected: `kbo doctor` exits 0. (If the publish output binary is named `kbo` rather than `Kbo`, install that one — check `ls /tmp/kbo-publish/` and match whatever name the previous deploy used.)

- [ ] **Step 5: Regenerate on real data and verify the claims**

```bash
kbo rebuild
kbo report
```
Then check `~/Knowledge/_generated/kbo-report.md` against four concrete expectations:
1. Inventory has **no** `repo-kb-observability-private-archive` line (was 69 notes).
2. A `## Lifecycle artifacts` section exists; `repo-CareerPlatform` shows roughly 40–60 notes (its plans+specs+journal files).
3. A `## Dormant sources` section lists `repo-CareerPlatform` (last activity ≈ 2026-07-15) with its withheld count — **provided** it has stayed inactive; if work resumed there since, it is legitimately active and stays on the worklist.
4. The dead worklist is dramatically shorter (expect roughly 10–30 rows, all reference notes in active sources) — read every remaining row and confirm each one is a *plausible* ritual candidate. If any row is still obvious noise, it is a new category this plan missed: record it in `docs/backlog.md`, do not patch ad hoc.

- [ ] **Step 6: Final commit and wrap-up**

```bash
git add -A && git status   # verify only intended files
git commit -m "chore: verify signal-over-noise report on live data" --allow-empty
```
Log the outcome (one line: dead-list before → after) in `docs/journal/2026-08-15.md` per the repo's Agent Notes convention.

---

### Task 6: Cadence-aware dead-man threshold (fix structural false reds)

> **Ordering note:** code-wise independent of Tasks 1–4, but it changes the deployed binary — if Task 5's deploy/verify steps already ran, repeat Task 5 Steps 4–5 after this task so the live dashboard picks it up.

**Files:**
- Create: `src/Kbo/Jobs/DeadMan.cs`
- Modify: `src/Kbo/Cli/PulseCommand.cs` (job list, ~lines 58–88)
- Modify: `src/Kbo/Gold/DashboardComputer.cs` (`DeadManThresholdDays` const at line 12; `JobHealth` at ~lines 340–359; `Compute`'s `DashboardGold` construction)
- Modify: `src/Kbo/Gold/DashboardGold.cs` (`JobHealthTile` record, drop the global `DeadManThresholdDays` field)
- Modify: `src/Kbo/Gold/DashboardRenderer.cs` (heading ~line 90, RU explainer ~line 92, tile status line ~line 154)
- Modify: `CHANGELOG.md`
- Test: `tests/Kbo.Tests/DashboardComputerTests.cs`, `tests/Kbo.Tests/DashboardRendererTests.cs`

**Interfaces:**
- Consumes: existing `JobCadence` enum (`Daily`, `Weekly`) from `src/Kbo/Jobs/IPulseJob.cs`.
- Produces:
```csharp
// src/Kbo/Jobs/DeadMan.cs
public static class DeadMan
{
    public const int DailyThresholdDays = 3;
    public const int WeeklyThresholdDays = 8;   // weekly due at 6.5d + 1.5d slack
    public static int ThresholdDaysFor(JobCadence cadence);
}
// src/Kbo/Cli/PulseCommand.cs — single authoritative name→cadence map,
// consumed by both the job list below it and DashboardComputer:
public static readonly IReadOnlyDictionary<string, JobCadence> JobCadences;
```
`JobHealthTile` gains an `int ThresholdDays` field (after `DaysSilent`, before `Status`); `DashboardGold` loses its global `DeadManThresholdDays` field (it would now lie). Unknown job names (events from retired jobs) default to `Daily` — the conservative side: flags too early, never too late.

- [ ] **Step 1: Write the failing tests** (append to `DashboardComputerTests.cs`, reusing its existing fixture helpers for `job.completed` events — same temp-workspace + `BronzeStore`/`SilverRebuilder` pattern as the rest of the file; only the assertions below are normative)

```csharp
[Fact]
public void Weekly_job_silent_five_days_is_ok()
{
    // one job.completed event: agent "kbo", subject "report", time = Now - 5d
    DashboardGold gold = ComputeWithJobEvent("report", daysAgo: 5);
    JobHealthTile tile = Assert.Single(gold.JobHealth, t => t.Job == "report");
    Assert.Equal("ok", tile.Status);
    Assert.Equal(DeadMan.WeeklyThresholdDays, tile.ThresholdDays);
}

[Fact]
public void Weekly_job_silent_nine_days_is_red()
{
    DashboardGold gold = ComputeWithJobEvent("report", daysAgo: 9);
    Assert.Equal("red", Assert.Single(gold.JobHealth, t => t.Job == "report").Status);
}

[Fact]
public void Daily_job_silent_four_days_is_red()
{
    DashboardGold gold = ComputeWithJobEvent("harvest", daysAgo: 4);
    Assert.Equal("red", Assert.Single(gold.JobHealth, t => t.Job == "harvest").Status);
}

[Fact]
public void Unknown_job_defaults_to_daily_threshold()
{
    DashboardGold gold = ComputeWithJobEvent("some-retired-job", daysAgo: 4);
    JobHealthTile tile = Assert.Single(gold.JobHealth, t => t.Job == "some-retired-job");
    Assert.Equal("red", tile.Status);
    Assert.Equal(DeadMan.DailyThresholdDays, tile.ThresholdDays);
}

[Fact]
public void Cadence_map_declares_report_and_audit_weekly()
{
    Assert.Equal(JobCadence.Weekly, PulseCommand.JobCadences["report"]);
    Assert.Equal(JobCadence.Weekly, PulseCommand.JobCadences["audit"]);
    Assert.Equal(JobCadence.Daily, PulseCommand.JobCadences["harvest"]);
}
```
(If the file has no single-job helper, add `ComputeWithJobEvent(string job, int daysAgo)` locally, modeled on the file's existing event builders: a `job.completed` event with `machine`/`agent`/`subject`/`time` set, appended via `BronzeStore`, rebuilt, then `DashboardComputer.Compute`.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Kbo.Tests --filter "FullyQualifiedName~DashboardComputerTests" -v minimal`
Expected: FAIL — `DeadMan`, `PulseCommand.JobCadences`, and `ThresholdDays` do not exist (compile errors).

- [ ] **Step 3: Implement**

1. Create `src/Kbo/Jobs/DeadMan.cs`:
```csharp
namespace Kbo.Jobs;

/// <summary>
/// Dead-man silence thresholds per job cadence. A flat threshold paints
/// every weekly job red from day 3 to its next due run (~6.5d) — a
/// structural false positive that trains the owner to ignore red.
/// </summary>
public static class DeadMan
{
    public const int DailyThresholdDays = 3;
    public const int WeeklyThresholdDays = 8;

    public static int ThresholdDaysFor(JobCadence cadence)
    {
        return cadence == JobCadence.Weekly ? WeeklyThresholdDays : DailyThresholdDays;
    }
}
```
2. In `PulseCommand.cs`, add the authoritative map above the job-list construction and make the `CommandJob` constructions read from it so the two cannot drift:
```csharp
public static readonly IReadOnlyDictionary<string, JobCadence> JobCadences =
    new Dictionary<string, JobCadence>
    {
        ["harvest"] = JobCadence.Daily,
        ["harvest-opencode"] = JobCadence.Daily,
        ["rebuild"] = JobCadence.Daily,
        ["archive"] = JobCadence.Daily,
        ["vault-git"] = JobCadence.Daily,
        ["bronze-git"] = JobCadence.Daily,
        ["backup"] = JobCadence.Daily,
        ["report"] = JobCadence.Weekly,
        ["audit"] = JobCadence.Weekly,
    };
```
Replace the literal cadence arguments: `new CommandJob("harvest", JobCadences["harvest"], ...)`, `new CommandJob("report", JobCadences["report"], ...)`, etc. (`ArchiveJob`/`GitCommitJob`/`BackupJob` hardcode `Cadence => JobCadence.Daily` in their classes; the map entries for their names exist for the dashboard side — leave the classes untouched.)
3. In `DashboardComputer.cs`: delete the `DeadManThresholdDays` const (line 12) and its argument in the `DashboardGold` construction (line ~31); rewrite the tile loop body in `JobHealth`:
```csharp
DateTimeOffset last = AsUtc(row[3]);
double daysSilent = (now - last).TotalDays;
string jobName = (string)row[2]!;
int thresholdDays = DeadMan.ThresholdDaysFor(
    PulseCommand.JobCadences.TryGetValue(jobName, out JobCadence cadence) ? cadence : JobCadence.Daily);
tiles.Add(new JobHealthTile(
    (string)row[0]!, (string)row[1]!, jobName, last,
    Math.Round(daysSilent, 1),
    thresholdDays,
    daysSilent > thresholdDays ? "red" : "ok"));
```
Add `using Kbo.Cli;` and `using Kbo.Jobs;` as needed.
4. In `DashboardGold.cs`: add `int ThresholdDays` to `JobHealthTile` between `DaysSilent` and `Status`; remove `int DeadManThresholdDays` from the `DashboardGold` record.
5. In `DashboardRenderer.cs`: heading (line ~90) becomes
```csharp
$"<h2>Dead-man health — red past each job's cadence threshold ({DeadMan.DailyThresholdDays}d daily / {DeadMan.WeeklyThresholdDays}d weekly)</h2>");
```
update the Russian explainer line (~92) to the same wording (пороги по каденции задачи: {DailyThresholdDays}д daily / {WeeklyThresholdDays}д weekly), and in `AppendTile`'s status line include the per-tile threshold: `... — {daysSilent:0.#}d silent (red > {thresholdDays}d)` — pass `tile.ThresholdDays` through to `AppendTile` as a parameter. Fix all remaining compile errors from the removed `DashboardGold.DeadManThresholdDays` (renderer tests, any `DashboardGold` construction in tests) mechanically.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test tests/Kbo.Tests -v minimal`
Expected: PASS — including pre-existing dashboard tests updated for the new record shapes. Any old test that asserted a weekly job red at 3–8 days silent must flip to `ok`: that flip *is* the fix, verify the case and update the expectation.

- [ ] **Step 5: CHANGELOG entry**

Append under the same `## 2026-08-15` heading as Task 5's entry:
```markdown
- dashboard: dead-man threshold is cadence-aware (daily 3d, weekly 8d) —
  weekly `report`/`audit` no longer burn red between runs.
```

- [ ] **Step 6: Commit**

```bash
git add src/Kbo/Jobs/DeadMan.cs src/Kbo/Cli/PulseCommand.cs src/Kbo/Gold/ tests/Kbo.Tests/ CHANGELOG.md
git commit -m "fix(dashboard): cadence-aware dead-man threshold"
```

- [ ] **Step 7: Live verification** (after redeploy — Task 5 Steps 4–5)

```bash
kbo report
python3 -c "
import json
d = json.load(open('/home/admin/Knowledge/_generated/kbo-dashboard.gold.json'))
for t in d['jobHealth']:
    print(t['job'], t['daysSilent'], t.get('thresholdDays'), t['status'])"
```
Expected: `report` and `audit` show `ok` with threshold 8 (unless genuinely silent past 8 days); all daily jobs unchanged. Zero red tiles on a healthy day.

---

## Self-Review

- **Spec coverage:** Context names four noise categories → Task 1 (archive/glob), Tasks 2–3 (lifecycle), Task 4 (dormancy), Task 6 (dead-man false reds); Task 5 verifies the report claims on live data, Task 6 Step 7 verifies the dashboard. No-silent-caps honored: every exclusion surfaces as a count or section.
- **Placeholders:** none — all steps carry code, commands, or exact expected output. Two spots delegate to the executor's judgment deliberately and say so: reusing existing test-builder helpers in renderer tests, and the publish-artifact name check.
- **Type consistency:** `NoteRole.Of`/`Lifecycle`/`Reference` (Task 2) match Task 3 usage; the final `GoldReport` constructor is stated once in Task 4's Interfaces block and Task 3's partial change is a strict prefix of it; `DormantSource(SourceId, LastActivity, WithheldDeadNotes)` is used identically in computer, renderer, and tests.
