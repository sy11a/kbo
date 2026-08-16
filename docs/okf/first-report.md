---
type: Component
title: First report — dead notes, read counts, staleness (gold + renderers)
description: kbo report computes gold facts once from silver + the registry inventory, writes the gold JSON twin, and renders the wikilinked Markdown worklist into the vault's _generated/.
tags: [component, gold, report, renderer, vault]
timestamp: 2026-08-15T00:00:00Z
status: implemented
---

# First report (`kbo report`)

The *act* surface (Q7): worklists that wikilink to the notes they criticise. All numbers are computed exactly once in gold (P2); renderers contain zero computation. Implementation decisions: ADR-0009.

## Facts (v1)

| Worklist | Rule | Source |
|---|---|---|
| Dead notes/skills | in inventory ≥ 30 days AND zero reads in 60 days (G2-12: M=30, N=60); reference notes in active sources only (ADR-0034) | inventory (registry roots) minus reads (silver `events_preferred`) |
| Lifecycle artifacts | per-source counts of notes under `/superpowers/plans/`, `/superpowers/specs/`, `/journal/` — die on completion, never on the dead worklist (ADR-0034) | inventory × `NoteRole` |
| Dormant sources | sources with no *usage* (reads/context loads — writes don't count, ADR-0035) in 21 days; their dead notes are withheld and reported as a count with last activity (ADR-0034) | silver `events_preferred` (by subject + by direct repo containment) |
| Hot notes | top read counts in the 60-day window + all-time totals | silver `events_preferred` |
| Staleness | ≥ 3 reads in 60 days AND unmodified > 90 days (owner-confirmed start values, ritual-tunable) | reads × file mtime |

- **Inventory** (the denominator): every `*.md` under every registry root; age proxied by mtime (a recently edited note is not dead) — ADR-0009.
- Reads = `knowledge.read` + `context.loaded` with `kbroot != null`, from `events_preferred` (G2-6 preference inherited from silver).

## Outputs (all under `~/Knowledge/_generated/`, owner-confirmed)

- `kbo-report.md` — Markdown worklist; vault notes as `[[wikilinks]]`, non-vault roots (skills) as plain paths; prominent "generated at" (P5)
- `kbo-report.gold.json` — the gold twin, same facts, machine-readable (dashboard 2.4 reads this)
- `README.md` — the loud GENERATED marker: hand-edits die on next run

## Implementation

- `src/Kbo/Gold/NoteInventory.cs` — registry roots → note inventory
- `src/Kbo/Gold/NoteRole.cs` — pure path→role classifier (`reference` | `lifecycle`), mirror of `ContentKind` (ADR-0025 pattern)
- `src/Kbo/Gold/GoldComputer.cs` — silver + inventory → `GoldReport` (all numbers born here)
- `src/Kbo/Gold/MarkdownRenderer.cs` — `GoldReport` → Markdown, zero computation
- `src/Kbo/Cli/ReportCommand.cs` — `kbo report [--vault-out <dir>]`; requires silver (points at `kbo rebuild` if missing)

## Links

- [Silver](silver.md) — the only data source · [Registry](registry.md) — the denominator · [Glossary](glossary.md)
