# 0026. Reuse / ROI lens

## Status

accepted

## Context

Ranking notes by total read count is misleading: a note re-read many times within one session looks as important as one consulted across many separate pieces of work. The probe showed distinct-session *reach* is the truer signal of load-bearing knowledge, and that most notes are single-use (~61% read in only one session). This is the "keep / promote / prune" signal the weekly ritual needs (original Phase 3d). It must be scoped to actual notes, not code (ADR-0025).

## Decision

Add a **reuse lens** to the dashboard: `NoteReuse` (gold) ranks knowledge notes — `ContentKind.Knowledge`, registered, over the 60-day window — by **distinct-session reach** (then total reads), and computes a `ReuseSummary` (distinct notes read, single-use count, single-use rate). Rendered as a "Most-reused knowledge notes" table plus a single-use-ratio sentence framing the top as the load-bearing core and single-use notes as review candidates.

Reach uses `count(DISTINCT session)`; the lens is notes-only via `ContentKind`, so code reads swept in by whole-repo registration don't pollute it.

## Consequences

- The dashboard surfaces the genuinely load-bearing notes (distinct-session reach) separately from re-read-within-a-task volume, and quantifies the single-use tail — direct ritual input.
- Uses the registry-now resolution (ADR-0021) and `ContentKind` (ADR-0025); no schema or capture change, retroactive.
- "Single-use" is windowed: a note read once in the last 60 days is single-use even if heavily used earlier — intentional, the lens is about recent reuse.
