# 0009. First report: gold in the vault, mtime age proxy, v1 thresholds

## Status

Accepted (executor decisions within `07 - Feature Specs` §Reports, Q7, G2-10/G2-12; plan step 1.7; owner-confirmed items marked)

## Context

The spec fixes the worklists (dead: inventory ≥ 30d + zero reads in 60d; hot: top read counts; staleness: heavily read + long-unmodified), the render target (`~/Knowledge/_generated/`, wikilinked), and P2 (all numbers computed once in gold, renderers zero computation). Left open: gold's location, the note definition, the age basis, staleness numbers, and read semantics.

## Decision

1. **Gold twin lives beside the report** (*owner-confirmed 2026-08-12*): `~/Knowledge/_generated/kbo-report.gold.json` next to `kbo-report.md` plus a `README.md` GENERATED marker. Vault-git (2.6) will version gold history for free; the dashboard (2.4) reads the JSON from there.
2. **Note definition**: `*.md` under every registry root — vault notes and skills flow through the same code (the spec's "dead notes/skills" symmetry). Non-md artifacts join when a report question demands them (P8).
3. **Age basis is mtime**: "in inventory ≥ 30 days" is proxied by last-modified — Linux creation time is unsettable/unreliable, and a recently edited note is not dead by intent anyway. Recorded as a proxy, revisit if it misclassifies.
4. **Staleness start values** (*owner-confirmed 2026-08-12*): ≥ 3 reads in the 60-day window AND unmodified > 90 days; ritual-tunable constants in `GoldComputer`.
5. **Reads** = `knowledge.read` + `context.loaded` with `kbroot != null`, from `events_preferred` — the G2-6 harvest preference is inherited from silver, never re-implemented (P2).
6. **Wikilinks only for the vault**: notes under the global-layer root render as `[[vault-relative-path]]` (extension stripped); other roots (skills) as plain code paths — Obsidian cannot link outside the vault.
7. **Report requires silver**: `kbo report` fails with a pointer to `kbo rebuild` rather than rebuilding implicitly — layers stay separately observable (P7); pulse (2.1) will chain them.
8. **Hot notes cap**: top 20 by window reads (then total, then path) — a worklist, not a dump.

## Consequences

First real run: inventory 541 (`knowledge` 324, `cc-skills` 214, `oc-commands` 3), 164 dead, 20 hot, 0 stale. The dead list immediately surfaces entire never-read skill packs — exactly the retire/fix-triggers conversation the ritual (1.8) exists for. All thresholds are constants awaiting their first ritual tuning; changing them is a code change recorded here, not silent drift.
