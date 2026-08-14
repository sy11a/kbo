# Atomic Silver Rebuild (ADR-0032) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `kbo rebuild` derives silver into a temp file and atomically swaps it in, and all gold readers open silver read-only — removing DuckDB lock contention between `watch`, `rebuild`, `report`, and `pulse` structurally (ADR-0032).

**Architecture:** `SilverRebuilder.Rebuild` keeps its public API (`Rebuild(eventsRepoRoot, silverPath) → RebuildResult`) but builds into `silver.duckdb.tmp-<guid>` and renames over the live file after closing the connection; stale temp files (>1h) are swept at rebuild start. A new `SilverConnection.OpenReadOnly` helper centralizes reader opens with `ACCESS_MODE=READ_ONLY` and a clear missing-file error; the four gold computers switch to it.

**Tech Stack:** .NET 10 (`net10.0`), DuckDB.NET.Data, xUnit 2.9.3. Repo root: `/home/admin/Repository/kbo`, solution `kb-observability.slnx`.

## Global Constraints

- Branch: `feature/atomic-silver-swap` (already exists, holds the ADR-0032 docs commit). Work and commit there.
- **NEVER add AI co-author trailers or any AI attribution to commits** (user's global rule — overrides any harness default).
- No new NuGet dependencies.
- Spec: `docs/adr/0032-atomic-silver-swap.md` — temp file pattern is `<silver filename>.tmp-<suffix>` in the same directory; stale-sweep threshold is **1 hour**; readers open with `ACCESS_MODE=READ_ONLY`.
- Verification gate (repo rule): zero build errors, zero new warnings, all tests green. Test command: `dotnet test` from repo root. Report failures verbatim.
- Code style: file-scoped namespaces, explicit types (no `var` in this codebase), xml-doc `<summary>` only where the code can't say it.

---

### Task 1: SilverRebuilder — temp build + atomic swap + stale sweep

**Files:**
- Modify: `src/Kbo/Silver/SilverRebuilder.cs` (the `Rebuild` method, lines ~71–121)
- Test: `tests/Kbo.Tests/SilverRebuilderTests.cs` (append new tests; existing tests must keep passing unchanged)

**Interfaces:**
- Consumes: existing `SilverRebuilder.Rebuild(string eventsRepoRoot, string silverPath)` and `RebuildResult(long EventCount, long SessionCount, long SkippedLines)` — signatures unchanged.
- Produces: same public API; new observable behavior — live silver replaced atomically, `<name>.tmp-*` siblings older than 1h deleted at rebuild start. Task 2's test `Rebuild` calls rely on this.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Kbo.Tests/SilverRebuilderTests.cs` (inside the existing class; `workspace`, `eventsRepo`, `silverPath`, `SeedBronze`, `Open`, `Scalar` already exist there):

```csharp
[Fact]
public void Rebuild_LeavesNoTempFilesBehind()
{
    SeedBronze();
    SilverRebuilder.Rebuild(eventsRepo, silverPath);

    Assert.True(File.Exists(silverPath));
    Assert.Empty(Directory.GetFiles(workspace, "silver.duckdb.tmp-*"));
}

[Fact]
public void Rebuild_ReplacesSilver_WhileReadOnlyReaderHoldsOldFile()
{
    SeedBronze();
    SilverRebuilder.Rebuild(eventsRepo, silverPath);

    using DuckDBConnection reader = new($"Data Source={silverPath};ACCESS_MODE=READ_ONLY");
    reader.Open();

    SilverRebuilder.Rebuild(eventsRepo, silverPath);

    Assert.Equal(8, Scalar(reader, "SELECT count(*) FROM events"));
    using DuckDBConnection fresh = Open();
    Assert.Equal(8, Scalar(fresh, "SELECT count(*) FROM events"));
}

[Fact]
public void Rebuild_SweepsStaleTempFiles_KeepsFreshOnes()
{
    SeedBronze();
    string staleTemp = Path.Combine(workspace, "silver.duckdb.tmp-stale");
    string freshTemp = Path.Combine(workspace, "silver.duckdb.tmp-fresh");
    File.WriteAllText(staleTemp, "leftover from a killed rebuild");
    File.WriteAllText(freshTemp, "a concurrent rebuild's live temp");
    File.SetLastWriteTimeUtc(staleTemp, DateTime.UtcNow.AddHours(-2));

    SilverRebuilder.Rebuild(eventsRepo, silverPath);

    Assert.False(File.Exists(staleTemp));
    Assert.True(File.Exists(freshTemp));
}
```

Note: xUnit constructs the class per test, so each test gets its own `workspace` — the leftover `freshTemp` cannot pollute `Rebuild_LeavesNoTempFilesBehind`.

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SilverRebuilderTests" 2>&1 | tail -20`
Expected: `Rebuild_LeavesNoTempFilesBehind` PASSES already (current code writes in place — that's fine, it pins the invariant); `Rebuild_ReplacesSilver_WhileReadOnlyReaderHoldsOldFile` FAILS (in-place delete+rebuild conflicts with the open read-only handle / deletes the file under it); `Rebuild_SweepsStaleTempFiles_KeepsFreshOnes` FAILS (`staleTemp` still exists).

- [ ] **Step 3: Implement temp-and-swap in SilverRebuilder**

In `src/Kbo/Silver/SilverRebuilder.cs`, replace the body of `Rebuild` and extract the existing derivation into `BuildInto`. The current method starts with `File.Delete(silverPath)` — that disappears entirely. New code:

```csharp
public static RebuildResult Rebuild(string eventsRepoRoot, string silverPath)
{
    string silverDirectory = Path.GetDirectoryName(Path.GetFullPath(silverPath))!;
    Directory.CreateDirectory(silverDirectory);
    string silverFileName = Path.GetFileName(silverPath);
    SweepStaleTempFiles(silverDirectory, silverFileName);

    // Build into a sibling temp file, then atomically rename over the live
    // silver (ADR-0032): the live file is never write-locked by a rebuild and
    // never observable half-built. Unique suffix so concurrent rebuilds
    // (watch tick + hourly pulse) each build their own temp; last swap wins.
    string tempPath = Path.Combine(
        silverDirectory, $"{silverFileName}.tmp-{Guid.NewGuid():N}");
    try
    {
        RebuildResult result = BuildInto(eventsRepoRoot, tempPath);
        File.Move(tempPath, silverPath, overwrite: true);
        return result;
    }
    catch
    {
        TryDelete(tempPath);
        throw;
    }
}

/// <summary>
/// A rebuild killed mid-flight leaves its temp file behind; the next rebuild
/// sweeps them. The 1-hour age guard protects a concurrent rebuild's live
/// temp (a rebuild takes seconds, not hours).
/// </summary>
private static void SweepStaleTempFiles(string silverDirectory, string silverFileName)
{
    DateTime cutoff = DateTime.UtcNow.AddHours(-1);
    foreach (string tempFile in Directory.EnumerateFiles(silverDirectory, silverFileName + ".tmp-*"))
    {
        if (File.GetLastWriteTimeUtc(tempFile) < cutoff)
        {
            TryDelete(tempFile);
        }
    }
}

private static void TryDelete(string path)
{
    try
    {
        File.Delete(path);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

private static RebuildResult BuildInto(string eventsRepoRoot, string databasePath)
{
    using DuckDBConnection connection = new($"Data Source={databasePath}");
    connection.Open();
    Execute(connection, CreateEventsTable);

    long eventCount = 0;
    long skippedLines = 0;
    string bronzeRoot = Path.Combine(eventsRepoRoot, "bronze");
    if (Directory.Exists(bronzeRoot))
    {
        using DuckDBTransaction transaction = connection.BeginTransaction();
        using DuckDBCommand insert = CreateInsertCommand(connection);
        foreach (string monthFile in Directory
            .EnumerateFiles(bronzeRoot, "*.ndjsonl", SearchOption.AllDirectories)
            .Order())
        {
            foreach (string line in File.ReadLines(monthFile))
            {
                if (InsertEvent(insert, line))
                {
                    eventCount++;
                }
                else
                {
                    skippedLines++;
                }
            }
        }
        transaction.Commit();
    }

    Execute(connection, CreateEventsPreferredView);
    Execute(connection, CreateSessionsView);

    using DuckDBCommand sessionCountCommand = connection.CreateCommand();
    sessionCountCommand.CommandText = "SELECT count(*) FROM sessions";
    long sessionCount = Convert.ToInt64(sessionCountCommand.ExecuteScalar(), CultureInfo.InvariantCulture);

    return new RebuildResult(eventCount, sessionCount, skippedLines);
}
```

`BuildInto`'s body is the existing lines 83–120 verbatim except the connection targets `databasePath`; the loop, views, and count query are unchanged. The `using` on the connection guarantees DuckDB checkpoints and drops its WAL before `File.Move` runs (the connection is disposed when `BuildInto` returns). The sweep pattern `<name>.tmp-*` also matches a leftover `<name>.tmp-<guid>.wal` from a killed rebuild.

- [ ] **Step 4: Run the full SilverRebuilder test class**

Run: `dotnet test --filter "FullyQualifiedName~SilverRebuilderTests" 2>&1 | tail -5`
Expected: all 8 tests PASS (5 pre-existing + 3 new). `Rebuild_IsDeterministic_P3Proof` in particular must still pass (it deletes and re-runs — unaffected by the swap).

- [ ] **Step 5: Commit**

```bash
git add src/Kbo/Silver/SilverRebuilder.cs tests/Kbo.Tests/SilverRebuilderTests.cs
git commit -m "Silver: rebuild into temp file and swap atomically (ADR-0032)"
```

---

### Task 2: SilverConnection.OpenReadOnly + convert the four gold readers

**Files:**
- Create: `src/Kbo/Silver/SilverConnection.cs`
- Modify: `src/Kbo/Gold/DashboardComputer.cs:21-22`, `src/Kbo/Gold/GoldComputer.cs:87-88`, `src/Kbo/Gold/AuditComputer.cs:105-106`, `src/Kbo/Gold/DailyDigestComputer.cs:22-23`
- Test: `tests/Kbo.Tests/SilverConnectionTests.cs` (new file)

**Interfaces:**
- Consumes: `SilverRebuilder.Rebuild` from Task 1 (only in tests).
- Produces: `public static DuckDBConnection SilverConnection.OpenReadOnly(string silverPath)` in namespace `Kbo.Silver` — returns an **opened** connection; throws `FileNotFoundException` (message `silver not found at <path> — run 'kbo rebuild' first`) when the file is missing.

- [ ] **Step 1: Write the failing tests**

Create `tests/Kbo.Tests/SilverConnectionTests.cs`:

```csharp
using System.Data.Common;
using DuckDB.NET.Data;
using Kbo.Silver;

namespace Kbo.Tests;

public class SilverConnectionTests : IDisposable
{
    private readonly string workspace;
    private readonly string silverPath;

    public SilverConnectionTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-silver-connection-tests").FullName;
        silverPath = Path.Combine(workspace, "silver.duckdb");
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private void CreateSilver()
    {
        using DuckDBConnection connection = new($"Data Source={silverPath}");
        connection.Open();
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE probe AS SELECT 42 AS answer";
        command.ExecuteNonQuery();
    }

    private static long QueryProbe(DuckDBConnection connection)
    {
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = "SELECT answer FROM probe";
        return Convert.ToInt64(
            command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void OpenReadOnly_MissingFile_ThrowsWithRebuildHint()
    {
        FileNotFoundException exception =
            Assert.Throws<FileNotFoundException>(() => SilverConnection.OpenReadOnly(silverPath));
        Assert.Contains("kbo rebuild", exception.Message);
        Assert.Contains(silverPath, exception.Message);
    }

    [Fact]
    public void OpenReadOnly_TwoConcurrentConnections_BothQuery()
    {
        CreateSilver();
        using DuckDBConnection first = SilverConnection.OpenReadOnly(silverPath);
        using DuckDBConnection second = SilverConnection.OpenReadOnly(silverPath);

        Assert.Equal(42, QueryProbe(first));
        Assert.Equal(42, QueryProbe(second));
    }

    [Fact]
    public void OpenReadOnly_RejectsWrites()
    {
        CreateSilver();
        using DuckDBConnection connection = SilverConnection.OpenReadOnly(silverPath);
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE illegal (id INTEGER)";

        Assert.ThrowsAny<DbException>(() => command.ExecuteNonQuery());
    }
}
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SilverConnectionTests" 2>&1 | tail -10`
Expected: compilation FAILS — `SilverConnection` does not exist yet. (A compile error in the test project is this cycle's "red".)

- [ ] **Step 3: Create SilverConnection**

Create `src/Kbo/Silver/SilverConnection.cs`:

```csharp
using DuckDB.NET.Data;

namespace Kbo.Silver;

/// <summary>
/// How gold readers open silver: read-only, so concurrent readers (watch's
/// dashboard compute, pulse's weekly report/audit) share the file instead of
/// taking exclusive locks (ADR-0032). Writing goes through SilverRebuilder only.
/// </summary>
public static class SilverConnection
{
    public static DuckDBConnection OpenReadOnly(string silverPath)
    {
        if (!File.Exists(silverPath))
        {
            throw new FileNotFoundException(
                $"silver not found at {silverPath} — run 'kbo rebuild' first", silverPath);
        }
        DuckDBConnection connection = new($"Data Source={silverPath};ACCESS_MODE=READ_ONLY");
        connection.Open();
        return connection;
    }
}
```

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SilverConnectionTests" 2>&1 | tail -5`
Expected: 3 PASS.

- [ ] **Step 5: Convert the four gold readers**

In each file, replace the two-line open with the helper (and add `using Kbo.Silver;` to the file's usings if not present):

`src/Kbo/Gold/DashboardComputer.cs` (~line 21), `src/Kbo/Gold/GoldComputer.cs` (~line 87), `src/Kbo/Gold/AuditComputer.cs` (~line 105), `src/Kbo/Gold/DailyDigestComputer.cs` (~line 22) — all four currently read:

```csharp
using DuckDBConnection connection = new($"Data Source={silverPath}");
connection.Open();
```

becomes:

```csharp
using DuckDBConnection connection = SilverConnection.OpenReadOnly(silverPath);
```

Do NOT remove `AuditComputer`'s existing `if (!File.Exists(silverPath)) return findings;` guard (~line 100) — audit deliberately degrades to no-findings when silver is absent; the helper's throw is the safety net for the other callers (`ReportCommand` already guards with its own `File.Exists` + the same error message; `WatchCommand` computes right after a rebuild).

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test 2>&1 | tail -5`
Expected: all tests PASS (the gold computer test fixtures build silver via `SilverRebuilder` or DuckDB directly, so files exist; read-only mode changes no query results).

- [ ] **Step 7: Commit**

```bash
git add src/Kbo/Silver/SilverConnection.cs src/Kbo/Gold/DashboardComputer.cs src/Kbo/Gold/GoldComputer.cs src/Kbo/Gold/AuditComputer.cs src/Kbo/Gold/DailyDigestComputer.cs tests/Kbo.Tests/SilverConnectionTests.cs
git commit -m "Gold: readers open silver read-only via SilverConnection (ADR-0032)"
```

---

### Task 3: Changelog, backlog, journal, final verification

**Files:**
- Modify: `CHANGELOG.md` (Unreleased → Changed section, add at top of that section)
- Modify: `docs/backlog.md` (delete the "Harden `kbo watch` vs silver lock contention" section — heading + paragraph, lines 7–9)
- Modify: `docs/journal/2026-08-14.md` (append an entry; if a different day, create/append that day's file per `docs/journal/README.md`)

**Interfaces:**
- Consumes: Tasks 1–2 landed and green.
- Produces: nothing code-visible; closes the docs loop the repo's constitution requires.

- [ ] **Step 1: Add the changelog entry**

At the top of the `### Changed` list under `## [Unreleased]` in `CHANGELOG.md`:

```markdown
- `kbo rebuild` is now atomic (ADR-0032): silver derives into a temp file and is renamed over `silver.duckdb`, so the live file is never write-locked by a rebuild and never observable half-built — concurrent `kbo watch`, `rebuild`, `report`, and the hourly pulse no longer fail with DuckDB "Conflicting lock" errors, and killing `watch` mid-rebuild leaves only a swept-up temp file instead of a viewless silver. Gold readers open silver read-only, so concurrent readers share the file.
```

- [ ] **Step 2: Remove the backlog item**

In `docs/backlog.md`, delete the section `## Harden \`kbo watch\` vs silver lock contention` (the heading and its single paragraph). Leave every other item untouched.

- [ ] **Step 3: Append the journal entry**

Append to `docs/journal/2026-08-14.md` (match the file's existing entry format) a paragraph covering: took the watch-lock-contention backlog item; considered skip-tick and lockfile coordination, chose structural removal via temp-and-swap (ADR-0032); rebuild now swaps atomically and gold readers open read-only via new `SilverConnection`; noted the ~16s lock window measurement that motivated it.

- [ ] **Step 4: Full verification gate**

Run: `dotnet build 2>&1 | tail -5` — Expected: 0 errors, 0 warnings.
Run: `dotnet test 2>&1 | tail -5` — Expected: all tests pass, 0 failed.
Optionally exercise for real: `KBO_SILVER=/tmp/claude-1000/-home-admin-Repository-kb-observability/28a4f0ba-be80-45a2-9e6d-bee15ffbc62e/scratchpad/silver-check.duckdb dotnet run --project src/Kbo -- rebuild --events-repo <events repo if present>` — skip if no local events repo.

- [ ] **Step 5: Commit**

```bash
git add CHANGELOG.md docs/backlog.md docs/journal/2026-08-14.md
git commit -m "Close the watch lock-contention backlog item (ADR-0032)"
```
