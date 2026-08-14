---
type: Component
title: Harvest — Claude Code transcript miner
description: kbo harvest claude-code — mines transcripts into the same envelope events as live capture; backfill, gap recovery, and authoritative values (Q4, G2-6).
tags: [component, harvest, backfill, bronze, claude-code]
timestamp: 2026-08-14T00:00:00Z
status: implemented
---

# Harvest (transcript miner)

Q4 pattern: hooks are primary, the miner is verification and recovery. `kbo harvest claude-code` re-derives events from `~/.claude/projects/*/*.jsonl` transcripts. Implementation decisions: ADR-0007.

## What it mines (v1)

| Transcript evidence | Event | Backfill value |
|---|---|---|
| first session record | `session.started` | historical `gitBranch` (→ `task`), `model` (first assistant), summed `usage` (deduped by `requestId`) |
| assistant `tool_use` Read | `knowledge.read` | `model` from the enclosing assistant record; `contenthash` stays null (historical bytes unknowable — ADR-0007) |
| assistant `tool_use` Grep/Glob | `knowledge.searched` | **authoritative `hits`** from the paired `toolUseResult` (G2-6; silver prefers these over hook best-effort) |
| assistant `tool_use` Write/Edit/NotebookEdit | `knowledge.written` | content fields stripped from `raw` and replaced by `<field>_size`; `contenthash` stays null like mined reads (ADR-0030) |
| assistant `tool_use` Skill | `skill.invoked` | `data.skill` = invoked skill; harvest-only, not in the live hook matcher (ADR-0024) |

- `context.loaded` is **hook-only** (owner-confirmed 2026-08-12): implicit loads are not tool activity in transcripts; reconstruction would hash today's disk state against yesterday's sessions.
- Every harvest event carries `data.origin: "harvest"`; live hook events carry `data.origin: "hook"`. Silver dedups per session preferring harvest (G2-6).

## Idempotency

File-granular, stateless (ADR-0007): every harvest event carries `data.transcript` (the transcript file stem); before mining, harvest scans bronze for stems that already have harvest-origin events and skips those files. Re-runs are no-ops (verified over 784 real transcripts); no ledger beside bronze. Session ids are NOT the unit — continuation/compacted files share session ids and carry ids differing from their filename; one `session.started` is emitted per file and silver collapses them.

### Additive backfill (`--backfill-skills`)

When a new event type is added after transcripts were already harvested (e.g. `skill.invoked`, ADR-0024), the normal skip would leave history untouched. `kbo harvest claude-code --backfill-skills` re-mines **all** transcripts, keeps **only** `skill.invoked` events, and skips transcripts that already carry one (`BronzeStore.TranscriptsWithType`) — purely additive, idempotent, and never duplicates existing event types. A one-time step per new mined type.

## Implementation

- `src/Kbo/Adapters/ClaudeCode/TranscriptMiner.cs` — one transcript → events
- `src/Kbo/Schemas/EventEnvelope.cs` — shared envelope builder (hook + miner emit through one door)
- `src/Kbo/Bronze/BronzeStore.cs` — harvested-session scan
- `src/Kbo/Cli/HarvestCommand.cs` — `kbo harvest claude-code [--transcripts <dir>]`

## Links

- [Claude Code adapter](claude-code-adapter.md) · [Schema registry](schema-registry.md) · [Registry](registry.md)
