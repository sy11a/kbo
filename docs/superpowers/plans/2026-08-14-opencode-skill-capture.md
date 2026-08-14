# opencode `skill.invoked` capture — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mine opencode skill invocations (`part.tool = 'skill'`) into `skill.invoked` events and generalize `--backfill-skills` to `kbo harvest opencode`, per the approved spec `docs/superpowers/specs/2026-08-14-opencode-skill-capture-design.md`.

**Architecture:** Harvest-only (no live-plugin change). One new case in `OpencodeMiner`'s part-tool switch emits `skill.invoked` (subject = skill name, `data.{skill, raw}` — schema `skill.invoked/1` unchanged). `HarvestCommand` accepts `--backfill-skills` on both agent arms, sharing one filter helper; idempotency reuses `BronzeStore.TranscriptsWithType` (the ADR-0024 template).

**Tech Stack:** .NET / xUnit / Microsoft.Data.Sqlite (test fixtures) / System.Text.Json.Nodes.

## Global Constraints

- Docs before code (repo backlog rule): the ADR/okf task is Task 1 and commits first.
- Schema evolution is additive-only (P8): **no** change under `schemas/` — `skill.invoked/1` already fits.
- Bronze is immutable: backfill only appends, never rewrites.
- No changes to `adapters/opencode/kbo-capture.ts`, `CaptureCommand`, gold computers/renderers, or the golden corpus (all verified out of scope in the spec).
- Build: `dotnet build`. Test: `dotnet test`. All commits on branch `feature/opencode-skill-capture` (already checked out, carries the spec commit).
- No AI attribution in commits or PR bodies (user global rule).

---

### Task 1: Docs first — ADR-0033, okf updates, backlog removal

**Files:**
- Create: `docs/adr/0033-opencode-skill-invoked-capture.md`
- Modify: `docs/okf/opencode-adapter.md` (harvest section, ~line 29)
- Modify: `docs/okf/harvest.md` ("Additive backfill" section, ~line 31)
- Modify: `docs/backlog.md` (remove the "opencode skill capture" section)

**Interfaces:**
- Consumes: nothing (docs only).
- Produces: ADR-0033, referenced by later commit messages and the changelog entry in Task 4.

- [ ] **Step 1: Write ADR-0033**

Create `docs/adr/0033-opencode-skill-invoked-capture.md` (0032 is taken by atomic-silver-swap):

```markdown
# 0033. opencode skill.invoked capture

## Status

accepted

## Context

ADR-0024 added `skill.invoked` for Claude Code and scoped opencode out: "its store does not expose skill invocations the same way." That claim is now stale — opencode (verified on 1.18.16's session store) records skill invocations as ordinary tool parts: `part.data.type = "tool"`, `tool = "skill"`, `state.input.name` = the skill name, timestamps in `state.time`. The existing miner pattern applies directly. Sessions already stamped as harvested would never re-emit their skill parts, so history needs the additive-backfill template ADR-0024 established.

## Decision

- **OpencodeMiner** maps `skill` tool parts to `skill.invoked`: subject = skill name, `data` = `{skill, raw, origin: harvest, transcript}` — reusing `skill.invoked/1` unchanged (additive-only P8 untouched).
- **Harvest-only**, mirroring ADR-0024's rationale: the live plugin's `CAPTURED_TOOLS` stays as-is — editing the plugin is a user setup action, and skills data tolerates harvest lag. Silver's `events_preferred` view (ADR-0020) would reconcile a live path if one is ever added.
- **`--backfill-skills` is generalized**: valid on `kbo harvest opencode` too, with the same semantics — skip-set from `BronzeStore.TranscriptsWithType(skill.invoked)`, mined events filtered to `skill.invoked` only. The filter is hoisted so both agent arms share it.

## Consequences

- opencode skills appear in the daily digest "Skills used" and dashboard "Top skills" automatically — gold queries key on `type='skill.invoked'` agent-agnostically.
- One-time `kbo harvest opencode --backfill-skills` recovers the historical invocations back to their original days.
- Slash commands remain uncaptured for both agents — they leave no distinct trace in either store.
- Supersedes ADR-0024's "opencode out of scope" note; the additive-backfill template is now agent-generic.
```

- [ ] **Step 2: Update `docs/okf/opencode-adapter.md`**

In the "## Harvest (`kbo harvest opencode`)" section, the paragraph currently ends with:

```
`part` tool rows → `knowledge.*` with authoritative hits (`metadata.matches`/`count`) and `state.time.start` timestamps. Skips sessions bronze has seen (transcript stamps).
```

Replace that ending with:

```
`part` tool rows → `knowledge.*` with authoritative hits (`metadata.matches`/`count`) and `state.time.start` timestamps; `skill` parts → `skill.invoked` (`state.input.name` = skill name, ADR-0033) — deliberately **not** in the live plugin's `CAPTURED_TOOLS` (harvest-only, mirroring ADR-0024). Skips sessions bronze has seen (transcript stamps); `--backfill-skills` re-mines already-harvested sessions for `skill.invoked` only (see [Harvest](harvest.md) §Additive backfill).
```

- [ ] **Step 3: Update `docs/okf/harvest.md`**

Replace the "### Additive backfill (`--backfill-skills`)" section body:

```
When a new event type is added after transcripts were already harvested (e.g. `skill.invoked`, ADR-0024), the normal skip would leave history untouched. `kbo harvest claude-code --backfill-skills` re-mines **all** transcripts, keeps **only** `skill.invoked` events, and skips transcripts that already carry one (`BronzeStore.TranscriptsWithType`) — purely additive, idempotent, and never duplicates existing event types. A one-time step per new mined type.
```

with:

```
When a new event type is added after transcripts were already harvested (e.g. `skill.invoked`, ADR-0024), the normal skip would leave history untouched. `kbo harvest <agent> --backfill-skills` (valid for both `claude-code` and `opencode`, ADR-0033) re-mines **all** transcripts/sessions, keeps **only** `skill.invoked` events, and skips transcripts that already carry one (`BronzeStore.TranscriptsWithType`) — purely additive, idempotent, and never duplicates existing event types. A one-time step per new mined type, per agent.
```

- [ ] **Step 4: Remove the backlog item**

In `docs/backlog.md`, delete the whole section (heading, body paragraph, and its trailing `---` separator):

```
## opencode skill capture

Claude Code skills are captured (ADR-0024); opencode skill/command invocations are not (its session store doesn't expose them the same way). If it becomes worthwhile, extend `OpencodeMiner` to emit `skill.invoked` and backfill. Low priority — Claude Code is where skills predominantly run.

---
```

- [ ] **Step 5: Commit**

```bash
git add docs/adr/0033-opencode-skill-invoked-capture.md docs/okf/opencode-adapter.md docs/okf/harvest.md docs/backlog.md
git commit -m "Docs: ADR-0033 opencode skill.invoked capture (supersedes ADR-0024 scope note)"
```

---

### Task 2: OpencodeMiner — mine `skill` parts into `skill.invoked`

**Files:**
- Modify: `src/Kbo/Adapters/Opencode/OpencodeAdapter.cs` (constants only: `Payload` ~line 28, `Tools` ~line 41)
- Modify: `src/Kbo/Adapters/Opencode/OpencodeMiner.cs` (switch ~line 143, new `MapSkill` method)
- Test: `tests/Kbo.Tests/OpencodeMinerTests.cs`

**Interfaces:**
- Consumes: `EventTypes.SkillInvoked` (`"skill.invoked"`), `EventDataFields.Skill` (`"skill"`) — both already exist in `src/Kbo/Schemas/`.
- Produces: `OpencodeAdapter.Tools.Skill = "skill"` and `OpencodeAdapter.Payload.Name = "name"` (Task 3's fixture reuses the same part shape); `skill.invoked` envelopes from `OpencodeMiner.Mine` for any session containing `skill` parts.

- [ ] **Step 1: Extend the test fixture and write the failing test**

In `tests/Kbo.Tests/OpencodeMinerTests.cs`, append to `SeedDatabase()` (after the `prt_3` insert):

```csharp
JsonObject skillPart = new()
{
    ["type"] = "tool",
    ["tool"] = "skill",
    ["callID"] = "call-3",
    ["state"] = new JsonObject
    {
        ["status"] = "completed",
        ["input"] = new JsonObject { ["name"] = "grilling" },
        ["metadata"] = new JsonObject { ["name"] = "grilling", ["dir"] = "/skills/grilling", ["truncated"] = false },
        ["time"] = new JsonObject { ["start"] = BaseMs + 140_000, ["end"] = BaseMs + 140_050 },
    },
};
JsonObject namelessSkillPart = new()
{
    ["type"] = "tool",
    ["tool"] = "skill",
    ["callID"] = "call-4",
    ["state"] = new JsonObject
    {
        ["status"] = "completed",
        ["input"] = new JsonObject(),
        ["time"] = new JsonObject { ["start"] = BaseMs + 150_000 },
    },
};
Insert(connection, "INSERT INTO part VALUES ('prt_4','msg_1','ses_a',@t,@t,@data)", ("@t", BaseMs + 140_000), ("@data", skillPart.ToJsonString()));
Insert(connection, "INSERT INTO part VALUES ('prt_5','msg_1','ses_a',@t,@t,@data)", ("@t", BaseMs + 150_000), ("@data", namelessSkillPart.ToJsonString()));
```

Add the new test:

```csharp
[Fact]
public void Mine_EmitsSkillInvoked_FromSkillTool_SkippingNamelessOnes()
{
    List<JsonObject> events = OpencodeMiner.Mine(databasePath, new[] { "ses_a" }, registry, new Random(42));

    JsonObject skill = events.Single(e => (string?)e["type"] == "skill.invoked");
    Assert.Equal("grilling", (string?)skill["subject"]);
    Assert.Equal("grilling", (string?)skill["data"]!["skill"]);
    Assert.Null(skill["kbroot"]);
    Assert.Equal("2026-07-15T10:02:20Z", (string?)skill["time"]);
    Assert.Equal("harvest", (string?)skill["data"]!["origin"]);
    Assert.Equal("ses_a", (string?)skill["data"]!["transcript"]);
    Assert.Equal("skill", (string?)skill["data"]!["raw"]!["tool"]);
}
```

(`events.Single(...)` also proves the nameless part was skipped; the existing `Mine_EmitsSessionStartedWithAggregatedUsageAndModelId` test schema-validates every event including the new one.)

Update the exact-sequence assertion in `Mine_EmitsToolEvents_WithAuthoritativeHitsAndPartTimes` — the seeded skill part now appears:

```csharp
Assert.Equal(new[] { "session.started", "knowledge.read", "knowledge.searched", "skill.invoked" },
    events.Select(e => (string?)e["type"]).ToArray());
```

- [ ] **Step 2: Run the tests to verify the new one fails**

Run: `dotnet test --filter "FullyQualifiedName~OpencodeMinerTests"`
Expected: `Mine_EmitsSkillInvoked_FromSkillTool_SkippingNamelessOnes` FAILS (no `skill.invoked` in sequence → `Single` throws), and `Mine_EmitsToolEvents_WithAuthoritativeHitsAndPartTimes` FAILS on the updated array (miner drops the unmapped `skill` parts). Others PASS.

- [ ] **Step 3: Implement the miner mapping**

`src/Kbo/Adapters/Opencode/OpencodeAdapter.cs` — add constants:

```csharp
// in Payload, after Path:
public const string Name = "name";

// in Tools, after Edit:
public const string Skill = "skill";
```

`src/Kbo/Adapters/Opencode/OpencodeMiner.cs` — extend the switch in `MineParts`:

```csharp
MappedTool? mapped = tool switch
{
    OpencodeAdapter.Tools.Read => MapRead(input, directory, raw, registry),
    OpencodeAdapter.Tools.Grep or OpencodeAdapter.Tools.Glob => MapSearch(input, state, directory, raw, registry),
    OpencodeAdapter.Tools.Write or OpencodeAdapter.Tools.Edit => MapWrite(input, directory, raw, registry),
    OpencodeAdapter.Tools.Skill => MapSkill(input, raw),
    _ => null,
};
```

Add the map method (next to `MapWrite`):

```csharp
private static MappedTool? MapSkill(JsonObject input, JsonObject raw)
{
    string? skill = (string?)input[OpencodeAdapter.Payload.Name];
    if (skill is null)
    {
        return null;
    }
    return new MappedTool(EventTypes.SkillInvoked, skill, null, new JsonObject
    {
        [EventDataFields.Skill] = skill,
        [EventDataFields.Raw] = raw,
    });
}
```

(`origin`/`transcript` are stamped by the shared code after the switch, like every other case.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~OpencodeMinerTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Kbo/Adapters/Opencode/OpencodeAdapter.cs src/Kbo/Adapters/Opencode/OpencodeMiner.cs tests/Kbo.Tests/OpencodeMinerTests.cs
git commit -m "OpencodeMiner: mine skill parts into skill.invoked (ADR-0033)"
```

---

### Task 3: HarvestCommand — `--backfill-skills` on the opencode arm

**Files:**
- Modify: `src/Kbo/Cli/HarvestCommand.cs` (usage ~line 12, arg loop ~line 31, both mine loops ~lines 114–156)
- Test: `tests/Kbo.Tests/HarvestCommandTests.cs`

**Interfaces:**
- Consumes: `OpencodeMiner.Mine(databasePath, sessionIds, registry, random)` emitting `skill.invoked` (Task 2); `BronzeStore.TranscriptsWithType(EventTypes.SkillInvoked)` (exists, agent-agnostic).
- Produces: `kbo harvest opencode [--db <file>] --backfill-skills` CLI behavior (Task 4 runs it for real).

- [ ] **Step 1: Write the failing test**

In `tests/Kbo.Tests/HarvestCommandTests.cs`, add `using Microsoft.Data.Sqlite;` to the usings, and add these helpers (below `WriteReadAndSkillTranscript`):

```csharp
private void WriteOpencodeDatabase(string databasePath, string sessionId, string skillName)
{
    using SqliteConnection connection = new($"Data Source={databasePath}");
    connection.Open();
    using SqliteCommand create = connection.CreateCommand();
    create.CommandText = """
        CREATE TABLE session (
            id TEXT PRIMARY KEY, directory TEXT NOT NULL, agent TEXT, model TEXT,
            tokens_input INTEGER DEFAULT 0, tokens_output INTEGER DEFAULT 0,
            tokens_cache_read INTEGER DEFAULT 0, time_created INTEGER NOT NULL, time_updated INTEGER NOT NULL);
        CREATE TABLE part (
            id TEXT PRIMARY KEY, message_id TEXT, session_id TEXT NOT NULL,
            time_created INTEGER NOT NULL, time_updated INTEGER NOT NULL, data TEXT NOT NULL);
        """;
    create.ExecuteNonQuery();

    long baseMs = DateTimeOffset.Parse("2026-07-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
    using SqliteCommand insertSession = connection.CreateCommand();
    insertSession.CommandText = "INSERT INTO session VALUES (@id, @dir, 'build', '{\"id\":\"glm-5.1\"}', 0, 0, 0, @t, @t)";
    insertSession.Parameters.AddWithValue("@id", sessionId);
    insertSession.Parameters.AddWithValue("@dir", workspace);
    insertSession.Parameters.AddWithValue("@t", baseMs);
    insertSession.ExecuteNonQuery();

    JsonObject skillPart = new()
    {
        ["type"] = "tool",
        ["tool"] = "skill",
        ["callID"] = "call-1",
        ["state"] = new JsonObject
        {
            ["status"] = "completed",
            ["input"] = new JsonObject { ["name"] = skillName },
            ["time"] = new JsonObject { ["start"] = baseMs + 60_000 },
        },
    };
    using SqliteCommand insertPart = connection.CreateCommand();
    insertPart.CommandText = "INSERT INTO part VALUES ('prt_1', 'msg_1', @session, @t, @t, @data)";
    insertPart.Parameters.AddWithValue("@session", sessionId);
    insertPart.Parameters.AddWithValue("@t", baseMs + 60_000);
    insertPart.Parameters.AddWithValue("@data", skillPart.ToJsonString());
    insertPart.ExecuteNonQuery();
}

private int RunOpencode(string databasePath, params string[] extraArgs)
{
    string? Environment(string name) => name switch
    {
        "KBO_REGISTRY" => registryPath,
        "KBO_EVENTS_REPO" => eventsRepo,
        _ => null,
    };
    string[] args = new[] { "opencode", "--db", databasePath }.Concat(extraArgs).ToArray();
    return HarvestCommand.Run(args, output, error, Environment, workspace);
}
```

Change `Dispose()` to clear SQLite pools before deleting the workspace (same pattern as `OpencodeMinerTests`):

```csharp
public void Dispose()
{
    SqliteConnection.ClearAllPools();
    Directory.Delete(workspace, recursive: true);
}
```

Add the test:

```csharp
[Fact]
public void BackfillSkills_Opencode_AddsOnlySkillInvoked_ToAlreadyHarvestedSessions_Idempotently()
{
    string databasePath = Path.Combine(workspace, "opencode.db");
    WriteOpencodeDatabase(databasePath, "ses_oc", "grilling");
    // Simulate a pre-skill harvest: the session is already stamped but carries no skill.invoked.
    new BronzeStore(eventsRepo).Append(new[]
    {
        new JsonObject
        {
            ["type"] = "knowledge.read",
            ["time"] = "2026-07-01T09:00:00Z",
            ["subject"] = "/x.md",
            ["machine"] = "test-machine",
            ["agent"] = "opencode",
            ["session"] = "ses_oc",
            ["data"] = new JsonObject { ["origin"] = "harvest", ["transcript"] = "ses_oc" },
        },
    });

    Assert.Equal(0, RunOpencode(databasePath, "--backfill-skills"));

    string monthFile = Directory.EnumerateFiles(
        Path.Combine(eventsRepo, "bronze", "test-machine", "opencode")).Single();
    string[] afterBackfill = File.ReadAllLines(monthFile);
    Assert.Single(afterBackfill, l => l.Contains("\"skill.invoked\"") && l.Contains("\"skill\":\"grilling\""));
    // The session.started event was NOT re-mined (only skill.invoked is additive).
    Assert.DoesNotContain(afterBackfill, l => l.Contains("\"session.started\""));

    Assert.Equal(0, RunOpencode(databasePath, "--backfill-skills"));
    Assert.Equal(afterBackfill.Length, File.ReadAllLines(monthFile).Length);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~HarvestCommandTests"`
Expected: the new test FAILS — `RunOpencode(..., "--backfill-skills")` returns 1 (the flag is rejected on the opencode arm by the current arg loop). All existing tests PASS.

- [ ] **Step 3: Implement the generalized flag**

In `src/Kbo/Cli/HarvestCommand.cs`:

Usage string (line 12) becomes:

```csharp
private const string Usage = $"usage: kbo harvest <{ClaudeCodeAdapter.AgentName} [--transcripts <dir>] | {OpencodeRetention.AgentName} [--db <file>]> [--backfill-skills]";
```

Arg loop: drop the agent guard on the flag arm —

```csharp
else if (args[index] == "--backfill-skills")
{
    backfillSkills = true;
}
```

(`--transcripts` stays claude-code-only, `--db` stays opencode-only.)

Hoist the backfill filter into a local function next to `AppendValidated`:

```csharp
List<JsonObject> FilterForBackfill(List<JsonObject> mined) =>
    backfillSkills
        ? mined.Where(minedEvent => (string?)minedEvent[EnvelopeFields.Type] == EventTypes.SkillInvoked).ToList()
        : mined;
```

Claude Code branch — replace the inline filter block:

```csharp
List<JsonObject> mined = TranscriptMiner.Mine(File.ReadLines(transcriptPath), transcriptId, registry, Random.Shared);
AppendValidated(transcriptPath, FilterForBackfill(mined));
```

(the old `if (mined.Count == 0) continue;` is subsumed — `AppendValidated` already no-ops on empty lists, so harvested/event counts behave identically.)

opencode branch — filter the same way:

```csharp
foreach (string sessionId in pendingSessions)
{
    AppendValidated(sessionId, FilterForBackfill(OpencodeMiner.Mine(databasePath, new[] { sessionId }, registry, Random.Shared)));
}
```

(The skip-set is already computed agent-agnostically at line 79: `backfillSkills ? store.TranscriptsWithType(EventTypes.SkillInvoked) : store.HarvestedTranscripts()` — no change needed there.)

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test`
Expected: all PASS — including the existing claude-code backfill test (`BackfillSkills_AddsOnlySkillInvoked_ToAlreadyHarvestedTranscripts_Idempotently`), which exercises the hoisted filter path.

- [ ] **Step 5: Commit**

```bash
git add src/Kbo/Cli/HarvestCommand.cs tests/Kbo.Tests/HarvestCommandTests.cs
git commit -m "harvest: accept --backfill-skills on the opencode arm (ADR-0033)"
```

---

### Task 4: Changelog, journal, and rollout

**Files:**
- Modify: `CHANGELOG.md` (`[Unreleased] → ### Added`)
- Modify: `docs/journal/2026-08-14.md` (append; create from `docs/journal/README.md`'s format only if missing)

**Interfaces:**
- Consumes: the working CLI from Task 3.
- Produces: the finished branch, ready for `superpowers:finishing-a-development-branch`.

- [ ] **Step 1: Add the changelog entry**

Under `## [Unreleased]` / `### Added`, add as the first bullet:

```markdown
- opencode skill capture (ADR-0033): `skill.invoked` is now mined from the opencode session store too (`skill` tool parts) — opencode's store gained skill invocations since ADR-0024 scoped it out. `kbo harvest opencode --backfill-skills` recovers historical invocations from already-harvested sessions (additive, idempotent), so opencode skills appear on past day pages and the dashboard's "Top skills used".
```

- [ ] **Step 2: Append the journal entry**

Append to `docs/journal/2026-08-14.md` a short section:

```markdown
## opencode skill.invoked capture (ADR-0033)

Closed the "opencode skill capture" backlog item. Key finding: ADR-0024's "opencode doesn't expose skill invocations" was stale — opencode 1.18.16 records them as ordinary `part` rows (`tool='skill'`, `state.input.name`), verified against the live store before designing. Went harvest-only (no plugin edit, mirroring ADR-0024's live-hook deferral) and generalized `--backfill-skills` to both agents by hoisting the skill filter in `HarvestCommand`. No schema/gold/golden-corpus changes — all verified agent-agnostic beforehand. Spec: `docs/superpowers/specs/2026-08-14-opencode-skill-capture-design.md`.
```

- [ ] **Step 3: Verify the build and full suite one last time**

Run: `dotnet build && dotnet test`
Expected: clean build, all tests PASS.

- [ ] **Step 4: Commit**

```bash
git add CHANGELOG.md docs/journal/2026-08-14.md
git commit -m "Changelog + journal: opencode skill capture (ADR-0033)"
```

- [ ] **Step 5: Rollout (after merge — manual, on this machine)**

Not part of the branch. After the PR merges:

```bash
dotnet publish src/Kbo -c Release -p:PublishSingleFile=true --self-contained false -o ~/.local/bin
kbo harvest opencode --backfill-skills
```

Expected output: `harvested 1 session(s), 3 event(s); ...` (the single session currently holding the 3 historical skill parts: 2× `grilling`, 1× `resolving-merge-conflicts`; all other sessions are skipped or yield no skills). Digest/dashboard pick the skills up on the next pulse rebuild.

---

## Self-review notes

- Spec coverage: docs (Task 1), miner (Task 2), CLI backfill (Task 3), rollout + changelog (Task 4). Schema/gold/golden/plugin: no-change constraints captured in Global Constraints. ✓
- The seeded `skill` part reuses the exact live-store shape observed on this machine (`input.name`, `metadata.{name,dir,truncated}`). ✓
- Type consistency: `OpencodeAdapter.Payload.Name` / `Tools.Skill` defined in Task 2 and reused (as raw part JSON) by Task 3's fixture. `FilterForBackfill` only exists in Task 3. ✓
