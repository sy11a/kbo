# 0021. Registry-now semantics for dashboard knowledge classification

## Status

accepted

## Context

The owner asked why the reads-by-layer chart was empty for Aug 10–12 despite heavy documented work. Cause: the chart (and KB-touch) classified events by the `kbroot` stamp written at capture time against that day's registry — the repo docs roots were registered later (ADR-0019), and stamps are frozen (bronze immutable, rebuild copies them). Meanwhile the theme chart resolved paths through the current registry at report time — two charts on one page answered "is this registered knowledge?" with different rules.

## Decision

Dashboard gold classifies knowledge by resolving event **subjects through the current registry at report time** (the rule ThemeReads and GoldComputer's inventory-path matching already use):

- **Reads by layer**: subject → source → layer; the `kbroot` column is no longer consulted.
- **KB-touch**: a session touched the KB if any of its events has a subject resolving under the current registry, **or** carries a `kbroot` stamp whose source id is still registered (fallback for `knowledge.searched`, whose subject is the query, not a path).

The capture-time stamp stays in bronze and silver untouched — it remains the historical record of what the registry said at capture (P7), and adapters keep stamping it.

## Consequences

- History fills in retroactively when roots are registered: Aug 10–12 gained 12/20/32 local reads, and KB-touch corrected from 0% to 75%/33%/24% on those days.
- All knowledge charts now share one classification rule: "registered *now*".
- Symmetric: unregistering a root removes its history from these charts (registry-now cuts both ways) — intentional, the dashboard reflects the current corpus.
- Slightly more gold-side work per report (per-subject resolution in C# instead of a SQL kbroot filter) — negligible at current volumes.
