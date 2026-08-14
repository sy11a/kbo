# opencode `skill.invoked` capture — design

Date: 2026-08-14
Status: approved (brainstorm with owner)
Backlog item: "opencode skill capture"

## Problem

Claude Code skill invocations are captured (`skill.invoked`, ADR-0024); opencode ones are not. ADR-0024 scoped opencode out with "its store does not expose skill invocations the same way" — that claim is now stale. Verified against the live store (opencode 1.18.16, `~/.local/share/opencode/opencode.db`): skill invocations appear in the `part` table as ordinary tool parts —

```
data.type = "tool", data.tool = "skill",
data.state.input.name = "<skill name>",   -- e.g. "grilling"
data.state.time.start = <unix ms>
```

So the existing miner pattern applies directly. Three historical invocations exist in the live DB today; they sit in sessions already stamped as harvested, so without a backfill path they would never be captured.

## Decisions (made during brainstorm)

1. **Harvest-only.** No change to the live plugin (`adapters/opencode/kbo-capture.ts` keeps `CAPTURED_TOOLS` as-is). Mirrors ADR-0024's rationale: skills data is digest-lag-tolerant, and a plugin edit is a user setup action. Silver's `events_preferred` view would reconcile a future live path if ever added.
2. **Generalize `--backfill-skills`.** The additive-backfill template from ADR-0024 (`BronzeStore.TranscriptsWithType`) is already agent-agnostic; the flag becomes valid on `kbo harvest opencode` too.

## Changes

### Docs (first, per backlog rule)

- **ADR-0033 "opencode skill.invoked capture"** — records the store finding, supersedes ADR-0024's out-of-scope note, states decisions 1–2.
- **`docs/okf/opencode-adapter.md`** — harvest section gains the `skill` part → `skill.invoked` mapping; note that `skill` is deliberately not in the live plugin's `CAPTURED_TOOLS`.
- **`docs/okf/harvest.md`** — "Additive backfill" section becomes agent-generic (`--backfill-skills` on both agents).
- **`docs/backlog.md`** — remove the item.

### Code

1. **`OpencodeAdapter.Tools`** gains `Skill = "skill"` — tool-name catalog only; `MapToolExecute` unchanged (harvest-only).
2. **`OpencodeMiner.MineParts`** — the tool switch gains a `skill` case returning
   `MappedTool(EventTypes.SkillInvoked, subject: input.name, kbroot: null, data: { skill: input.name, raw })`.
   The shared tail stamps `origin: harvest` and `transcript: <session id>` as for every other case. Missing/null `input.name` → part skipped (consistent with the other maps). Timestamp from `state.time.start` via the existing guard.
3. **`HarvestCommand`** — `--backfill-skills` accepted for the opencode agent:
   - usage string: `--backfill-skills` listed on both agent arms;
   - skip-set: `TranscriptsWithType(EventTypes.SkillInvoked)` instead of `HarvestedTranscripts()` (same as claude-code);
   - mined events filtered to `skill.invoked` only before append, applied identically on both agent branches (hoist the existing filter so it is shared rather than claude-code-only).

### Not changed

- **Schema** — `skill.invoked/1` already requires exactly `data.{skill, raw}`; the opencode emission fits as-is (additive-only P8 untouched).
- **Gold** — daily digest "Skills used" and dashboard `TopSkills` query `type='skill.invoked'` agent-agnostically; opencode skills appear automatically.
- **Live plugin / CaptureCommand** — out of scope (decision 1).

## Testing

- **`OpencodeMinerTests`**: a session fixture containing a `skill` tool part mines to a `skill.invoked` envelope with `subject` = skill name, `data.skill`, `data.raw.tool = "skill"`, `origin = harvest`, `transcript` = session id; a `skill` part with missing `input.name` is skipped without error.
- **`HarvestCommandTests`**: `kbo harvest opencode --backfill-skills` on a store whose session is already stamped harvested appends only `skill.invoked` events (no duplicate `knowledge.*`/`session.started`); a second run appends nothing (idempotent via `TranscriptsWithType`); unknown flags still error on both agents.
- **Golden corpus**: unchanged. The corpus is per-type, not per-agent (opencode-emitted types such as `session.started` carry only claude-code examples today), and the opencode emission fits the existing `skill.invoked/1` example shape.

## Rollout

One-time `kbo harvest opencode --backfill-skills` on this machine after merge — recovers the 3 historical invocations back to their original days (digest/dashboard pick them up on next pulse rebuild).

## Out of scope

- Live hook capture of skills for opencode (plugin edit + reinstall; deferred exactly as ADR-0024 deferred the Claude Code live hook).
- opencode slash commands — they leave no distinct trace in the session store (verified: no command-shaped part types), same situation as Claude Code.
