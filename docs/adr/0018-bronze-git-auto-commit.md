# 0018. Bronze auto-commit (bronze-git job)

## Status

accepted

## Context

`BronzeStore` git-inits the events repo (`~/Repository/kb-events`) on first append, but nothing ever committed: after months of capture the repo had zero commits, so git provided no history and no tamper-evidence for the append-only bronze store — restic backup was the only safety net. Surfaced during a setup health check; the owner chose auto-commit over accepting restic-only.

## Decision

Generalize `VaultGitJob` into `GitCommitJob(name, root, …)` — identical behavior, configurable job name — and register it twice in the pulse: `vault-git` (unchanged, ADR-0013) and `bronze-git` on the events repo. `bronze-git` runs daily after harvest/archive and before backup, so restic snapshots capture the committed state. Same conventions as the vault: local-only (no remote — durability stays backup's job), inline identity `user.name=kbo`, message `kbo auto-commit <UTC ts>`. The job registers only when the events repo directory exists.

## Consequences

- Bronze gains point-in-time history and tamper-evidence: any rewrite of past NDJSON lines shows up as a diff against the committed history, supporting the append-only invariant (G2-7) instead of trusting it.
- The first commit also captures the pre-remediation `bronze-backup-2026-08-12/` directory — intentional, it is part of the repo's history.
- One implementation for both git jobs; a behavior change in one is a behavior change in both (accepted — they are meant to behave identically).
- The job's own `job.completed` event lands in bronze after the commit, so each day's commit includes the previous day's completion marker — a one-pulse lag, harmless for audit purposes.
