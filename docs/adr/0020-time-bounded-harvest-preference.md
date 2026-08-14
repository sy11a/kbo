# 0020. Time-bounded harvest preference in events_preferred

## Status

accepted (refines the `events_preferred` semantics of ADR-0008)

## Context

The owner noticed the reads-by-layer chart showed no events on days with visible agent activity. Diagnosis: `events_preferred` implemented G2-6 ("silver prefers harvest") at whole-session granularity — once any part of a session was harvested, *all* its hook events were dropped. Combined with file-granular harvest dedup (a transcript is mined once, then stamped and skipped forever — ADR-0007 §8), the tail of any session that keeps running after a daily harvest became permanently invisible: never re-harvested, and its hook events suppressed. Long-running sessions span the daily harvest routinely, so this silently lost real usage data from every chart and report while bronze held it correctly.

## Decision

Make the preference time-bounded: hook rows of a harvest-covered session are dropped only when their `time` is at or before the session's **last harvest-event time** (`max(time)` of the session's harvest-origin rows); newer hook rows — the live tail — stay visible. Harvest remains authoritative for everything it actually mined; hooks cover what it has not reached. `context.loaded` rows still always survive (hook-only, ADR-0007). Implemented as a `LEFT JOIN` against per-session `harvested_until` in the view; no schema change, applied by the next `kbo rebuild` (P3 — the view is derived, bronze untouched).

Rejected alternative (may still come later): incremental re-harvest of grown transcripts via line-offset stamps — more invasive (stamp semantics, miner, dedup), and only adds late token-usage refresh over what the time bound already restores.

## Consequences

- The live tail of long-running sessions is visible in every chart/report the same day it happens; the view self-heals if a later harvest (e.g. a continuation file) covers the tail — those hook rows drop out again automatically.
- Boundary skew: a read occurring within a few seconds of the last harvested moment could appear twice (once mined, once as a hook row timestamped just after) — bounded to the harvest boundary, accepted.
- Token usage for grown sessions still reflects only what harvest mined (usage arrives via `session.started` harvest rows); fixing that would require the rejected incremental re-harvest.
