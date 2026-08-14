# 0011. Audit: seen-transcripts from both origins, manifest session enumeration, findings as gold

## Status

Accepted (executor decisions within `07 - Feature Specs` §Completeness audit and §Registry; plan step 2.2)

## Context

The spec fixes the audit's two findings (session files bronze never saw; knowledge-looking reads under no registered root), the recovery path (harvest), and the weekly cadence from pulse. Left open: how "bronze has seen" is determined, how manifests enumerate sessions, and where findings land.

## Decision

1. **"Seen" spans both capture origins**: a transcript stem counts as seen when bronze holds either a harvest event stamped `data.transcript` (ADR-0007) or a hook event whose `raw.transcript_path` has that stem — so a session captured live but not yet harvested is not a false positive, and vice versa.
2. **Manifests enumerate sessions explicitly**: `RetentionManifest` gains an optional `SessionFiles` file-tree entry (adapter contract #3 sharpened). Claude Code sets it (`projects/**/*.jsonl`); opencode's stays null until its full adapter (2.3) — the audit reports such agents as *not session-auditable* rather than silently skipping them.
3. **Findings are gold**: `kbo-audit.md` + `kbo-audit.gold.json` in the vault's `_generated/` — same twin pattern and location as the report (ADR-0009); the health panel (2.4) reads the JSON. The missing-sessions line carries the spec's exact shape: agent, machine, count, "missing since" (earliest missing file's mtime), recovery command.
4. **Unregistered-knowledge finding** comes from silver's `events_preferred`: `knowledge.read` with `kbroot IS NULL` on `.md` subjects, grouped by directory, top 20 by read count. Missing silver degrades to an empty finding rather than failing — the session diff needs only bronze + disk.
5. **Exit code stays 0 when findings exist**: findings are the audit working, not the audit failing; the dead-man discipline (alert on silence) applies to the job itself via its `job.*` events. Wired into pulse as the second weekly job, after report.

## Consequences

Acceptance proven live: a planted never-captured transcript was flagged (`agent claude-code on example-machine: 2 session file(s) missing since 2026-08-12`) alongside a real gap the audit caught unprompted; `kbo harvest` recovered it and the re-audit reported clean. The unregistered-sources table immediately produced registry candidates (a skill's source dir, a project's `docs/okf`) — the "map goes stale" nag working as designed. When 2.3 gives opencode session enumeration, the audit covers it with zero audit-code changes.
