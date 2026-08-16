# 0036 — Machine-managed note role and per-source inventory excludePaths

Status: accepted · Date: 2026-08-16

## Context

After ADR-0034/0035 the live dead worklist held 35 rows, of which ~24 were
two residual noise categories: fleet-law files that the constitution
tooling writes into every repo (`docs/ai/rules/**`, `docs/adr/template.md`
scaffolding) — 12 rows across an application repo and others — and tool
data swept in by registering a skills repo whose root also contains eval
fixtures and benchmarks (`evals/**`, `.superpowers/**`) — 12 rows. Neither
is reference knowledge a ritual should prune.

## Decision

Split by where the convention lives:

1. **Machine-managed role (code)** — `NoteRole` gains `machine-managed`
   for fleet-wide conventions: any `/docs/ai/` path segment and the
   `/adr/template.md` suffix. Machine-managed notes never enter the dead
   worklist and are reported as per-source counts (no-silent-caps). The
   convention is fleet-wide (every constitution repo has these paths), so
   it belongs beside the lifecycle segments, per the ADR-0034 precedent.
2. **Per-source `excludePaths` (registry config)** — a source may declare
   relative subtrees (`excludePaths: [evals, .superpowers]`) that the
   note inventory skips entirely. Repo-specific layout is configuration,
   not code (no-hardcoded-paths principle). Entries must be relative and
   glob-free; invalid entries fail loudly. Inventory-only: `Resolve` and
   kbroot tagging are unaffected, so reads under excluded subtrees still
   count as source usage.

## Consequences

The dead worklist reduces to genuine reference-note anomalies. Excluded
fixtures leave the inventory denominator (like ADR-0034's glob
`exclude`); machine-managed files stay counted. If a future repo keeps
lifecycle or law files under unconventional paths, `excludePaths` covers
it without code changes.
