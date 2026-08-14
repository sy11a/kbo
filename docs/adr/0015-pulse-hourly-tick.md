# 0015. Pulse scheduling: hourly dumb tick, bronze decides due-ness

## Status

Accepted (owner decision 2026-08-12; refines ADR-0010 §2/§7)

## Context

ADR-0010 registered a daily `OnCalendar=daily` timer with `Persistent=true` — which covers a machine that is off at 00:00 (fires at next power-on) but not a pulse that runs and *fails*: nothing retried until the next day. The owner raised exactly this gap ("computer usually off at 00:00; retry within the day if a run failed").

## Decision

1. **The OS timer becomes a dumb hourly tick** (`OnCalendar=hourly`, `Persistent=true`). It carries no scheduling intelligence.
2. **Due-ness lives entirely in bronze** (extending ADR-0010's weekly mechanism to all jobs): `JobCadence.EveryPulse` becomes `JobCadence.Daily` — due when the last `job.completed` for that job name falls on an earlier **local calendar day** than now; weekly stays ≥ 6.5 days.
3. Consequences of that one rule, with no additional machinery:
   - machine off at 00:00 → the first tick after power-on runs everything due;
   - a **failed** job emits no `job.completed`, stays due, and retries on every hourly tick until it succeeds that day;
   - a completed day makes subsequent ticks near-no-ops (~1s: one bronze scan, all skips).
4. Local calendar day (via `TimeProvider.LocalTimeZone`) rather than a rolling 24h window: "ran today" matches the owner's mental model and avoids drift.

## Consequences

Verified live: post-cutover tick completed in 1.1s with all completed jobs skipped. The hourly tick writes "not due" lines to the journal 24×/day — acceptable noise; the job events in bronze stay one per actual run. The dead-man threshold (3 days) is untouched: it now signals "failed all retries for 3 days," a strictly stronger signal.
