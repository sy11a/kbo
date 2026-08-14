# 0027. Write → read loop lens

## Status

accepted

## Context

The system measures whether knowledge is *read*, but not whether knowledge *created by agents* gets reused — the actual flywheel of an AI-assisted practice. The probe showed 759 of 1,215 agent-written files were later read, a strong signal worth surfacing.

## Decision

Add a **write→read loop** lens to the dashboard: `WriteReadLoop` (gold) takes knowledge notes (`ContentKind.Knowledge`, registered) written in the 60-day window, finds each note's first write time, and counts reads that occurred **after** that write. A `WriteReadSummary` reports written-note count, the count later read, and the loop rate; a top table ranks notes by later-read count. Reads before a note's first write don't count (a genuine after-write test, not mere co-occurrence).

## Consequences

- The dashboard shows whether captured knowledge pays off (62% loop rate on real data) — a motivating, practice-level number no other surface provides.
- Notes-only (ADR-0025), registry-now (ADR-0021); no schema or capture change, retroactive.
- Windowed to writes in the last 60 days, so very recent writes that haven't had time to be read pull the rate down slightly — honest, and it recovers as they get read.
- Reuse lens (ADR-0026) answers "which knowledge is load-bearing"; this answers "does agent-produced knowledge get reused at all" — complementary.
