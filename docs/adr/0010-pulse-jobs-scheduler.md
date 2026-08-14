# 0010. Pulse: stateless due-ness from bronze, in-code job registry, systemd user timer, Phase 0 cutover

## Status

Accepted (executor decisions within `03 - Architecture` §Scheduling, G2-12, P5; plan step 2.1; owner-confirmed items marked)

## Context

The architecture fixes: one OS-scheduler entry → `kbo pulse`; every job emits `job.*` events; no resident daemon. G2-12 fixes cadences (pulse daily; archive/backup ride every pulse; report weekly). Left open: how due-ness is tracked, where the job registry lives, scheduler mechanics, and how the Phase 0 scripts hand over.

## Decision

1. **Job set v1** (*owner-confirmed 2026-08-12*): every pulse — `harvest` → `rebuild` → `archive` → `backup` (order matters: silver sees fresh events); weekly — `report`. `audit` joins in 2.2, vault auto-commit in 2.6.
2. **Due-ness is stateless (P5)**: weekly jobs are due when bronze's latest `job.completed` for their name is absent or ≥ 6.5 days old — the pipeline consults its own event stream; no schedule state file.
3. **Job registry is code** (`PulseCommand` builds `IPulseJob` instances): five entries don't justify a config format (P8); a config file appears when a machine needs a different set.
4. **Failure isolation**: a failing job emits `job.failed` (error message, `duration_ms: null`) and the pulse continues; exit code reflects failures but the dead-man panel alerts on silence, not errors.
5. **Archive keeps the Phase 0 contract** (*owner-confirmed 2026-08-12*): manifest-driven (adapter contract #3 — `ClaudeCodeRetention`, plus a minimal `OpencodeRetention` ahead of its 2.3 adapter), zstd via the system binary, same `~/Archive/agent-transcripts` layout and mtime idempotency — existing bash-era archives remain valid (verified live: 1,141 skipped, 128 new). opencode's SQLite gets a consistent copy via `Microsoft.Data.Sqlite`'s backup API (replacing the python one-liner) + one dated snapshot per ISO week; `auth.json` stays excluded.
6. **Backup replicates `kb-backup` exactly**: restic repo `~/Backups/kb-restic` (`KB_RESTIC_REPO`), password file `~/.config/kb-observability/restic-password`, paths = archive root + vault (global registry source) + kb-events, `forget --keep-daily 7 --keep-weekly 4 --keep-monthly 6 --prune`.
7. **Scheduler**: `kbo init` writes `~/.config/systemd/user/kbo-pulse.{service,timer}` (oneshot; `OnCalendar=daily`, `Persistent=true` so missed runs catch up after downtime — dead-man-friendly) and enables it. *Owner-approved cutover*: `kb-archive.timer`/`kb-backup.timer` are disabled with unit files kept (one command re-enables).
8. **Self-emitted events carry a human `summary`** in data (schema-open, additive): the job event doubles as the log line; no separate log files (`archive.log`/`kb-backup.log` retire with their scripts).

## Consequences

The Phase 0 scripts are replaced; `~/.local/bin/kb-archive`/`kb-backup` remain on disk as dormant fallbacks. First real pulse: full pipeline in ~18s (harvest 0.5s, rebuild 9.8s, archive 5.2s, backup 1.7s, report 0.1s); second pulse skips the weekly report. The reboot test (plan acceptance) is the owner's to run: `systemctl --user list-timers kbo-pulse.timer` after a reboot. The health panel (2.4) reads the same `job.*` events this step started emitting.
