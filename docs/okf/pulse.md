---
type: Component
title: Pulse — job registry, scheduler registration, self-observation
description: kbo pulse runs the registered jobs (harvest, rebuild, archive, vault-git, bronze-git, backup every pulse; report and audit weekly), emits job.* events into bronze, and kbo init registers the single systemd user timer.
tags: [component, pulse, jobs, scheduler, self-observation]
timestamp: 2026-08-13T00:00:00Z
status: implemented
---

# Pulse (`kbo pulse` + `kbo init`)

One OS-scheduler entry → `kbo pulse` → every registered job; every job run emits `job.completed`/`job.failed` into bronze — the pipeline is itself a monitored subject (P5). No resident daemon (NOT-list). Implementation decisions: ADR-0010.

## Job registry (v1, owner-confirmed)

| Job | Cadence | Does |
|---|---|---|
| harvest | daily | `kbo harvest claude-code` in-process |
| harvest-opencode | daily | `kbo harvest opencode` in-process (since 2.3) |
| rebuild | daily | `kbo rebuild` in-process (after harvest — silver sees fresh events) |
| archive | daily | transcript archive per adapter retention manifests (zstd, idempotent, same layout as Phase 0 `kb-archive`) |
| vault-git | daily | vault under local git, auto-commit (`kbo auto-commit <ts>`), before backup so snapshots capture the commit (ADR-0013) |
| bronze-git | daily | events repo under local git, auto-commit — tamper-evident history for append-only bronze; same `GitCommitJob` as vault-git, before backup (ADR-0018) |
| backup | daily | restic backup + forget policy (same repo/policy as Phase 0 `kb-backup`) |
| report | weekly | `kbo report` in-process |
| audit | weekly | `kbo audit` in-process (since 2.2) |

- **Due-ness is stateless (P5)**: every job consults bronze's last `job.completed` for its name — daily jobs run once per local calendar day, weekly past 6.5 days; no schedule state file. The OS timer is a dumb **hourly** tick (`Persistent=true`), so off-at-midnight machines catch up at power-on and failed jobs retry hourly until they succeed that day (ADR-0015).
- A failed job emits `job.failed` (with error) and the pulse continues; exit code reflects failures, the dead-man panel alerts on silence.

## Doctor (login-time self-check)

`kbo doctor [--notify]` — timer armed? every job completed within the 3-day dead-man threshold? Installed by `kbo init` as `kbo-doctor.service` (runs at every login, desktop notification: critical on problems, brief "healthy" otherwise). Same facts as the dashboard tiles, pushed instead of waited for (ADR-0016). Owner cheat sheet: `docs/operations.md`.

## Retention manifests (adapter contract #3)

- Claude Code: `~/.claude/projects/**/*.jsonl` + `history.jsonl` → `claude-code/…`.zst
- opencode (manifest only; full adapter is 2.3): SQLite consistent copy (`Microsoft.Data.Sqlite` backup API) → `opencode-latest.db.zst` + one dated snapshot per ISO week; `tool-output/` and `snapshot/` file trees
- Archive destination: `~/Archive/agent-transcripts` (`KB_ARCHIVE_ROOT` honored) — never the git data repo (P10)

## `kbo init`

Validates the registry, writes `~/.config/systemd/user/kbo-pulse.{service,timer}` (hourly tick, `Persistent=true`), reloads and enables the timer. Owner-approved cutover: Phase 0 `kb-archive.timer`/`kb-backup.timer` are disabled (unit files kept).

## Implementation

- `src/Kbo/Jobs/` — `IPulseJob`, `PulseRunner`, job implementations, `ProcessRunner`
- `src/Kbo/Adapters/*/RetentionManifest` pieces
- `src/Kbo/Cli/PulseCommand.cs`, `src/Kbo/Cli/InitCommand.cs`

## Links

- [Harvest](harvest.md) · [Silver](silver.md) · [First report](first-report.md) · [Claude Code adapter](claude-code-adapter.md)
