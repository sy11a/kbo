---
type: Component
title: Audit — capture completeness self-check
description: kbo audit diffs session transcripts on disk (retention manifests) against what bronze has seen, and flags reads of knowledge-looking files under no registered root.
tags: [component, audit, completeness, capture]
timestamp: 2026-08-12T00:00:00Z
status: implemented
---

# Audit (`kbo audit`)

A capture pipeline cannot verify itself from inside (Q4): the audit checks bronze against the independent source — session files on disk, enumerated via adapter retention manifests, never hardcoded paths. Implementation decisions: ADR-0011.

## Findings (v1)

| Finding | Rule | Recovery |
|---|---|---|
| Missing sessions | transcript file stems on disk (manifest `SessionFiles`) that bronze has never seen — neither as a harvest `data.transcript` stem nor as a hook event's `raw.transcript_path` stem — reported per agent with count + "missing since" (earliest file mtime) | `kbo harvest` re-ingests |
| Unregistered knowledge | `knowledge.read` events with `kbroot: null` on `.md` subjects, grouped by directory — "unregistered knowledge source?" (the registry is hand-maintained; the system must nag when its map goes stale) | add the root to `~/.config/kbo/registry.yaml` |

- SQLite-store agents are covered via the manifest's `SessionDatabase` entry (`SqliteSessionSource`, ADR-0014) — opencode sessions audit like files since 2.3.
- Findings are gold: `~/Knowledge/_generated/kbo-audit.md` + `kbo-audit.gold.json` (same twin pattern as the report); the health panel (2.4) reads the JSON.
- Runs weekly from pulse (G2-12), emitting `job.*` like every job.

## Implementation

- `RetentionManifest.SessionFiles` — the manifest's session-enumeration entry (adapter contract #3 sharpened)
- `src/Kbo/Bronze/BronzeStore.SeenTranscripts()` — stems bronze knows, from both origins
- `src/Kbo/Gold/AuditComputer.cs` + `AuditReport` — the facts
- `src/Kbo/Cli/AuditCommand.cs` — `kbo audit`; wired into pulse as a weekly job

## Links

- [Pulse](pulse.md) · [Harvest](harvest.md) — the recovery path · [Registry](registry.md) · [First report](first-report.md)
