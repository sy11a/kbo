# 0037 — Cadence-aware dead-man thresholds

Status: accepted · Date: 2026-08-16

## Context

The dead-man switch (G2-12) flagged any job silent for more than 3 days.
Weekly pulse jobs (`report`, `audit`) are *due* only after 6.5 days, so
every healthy weekly job went falsely SILENT in `kbo doctor` and red on
the dashboard from day 3 to day ~7 of every week — a guaranteed weekly
false alarm. Diagnosed 2026-08-16: both "silent" jobs had completed
normally on their last due day and were simply not due yet.

## Decision

The dead-man threshold is cadence-aware: a job is silent only when past
its cadence's due point plus the same 3-day grace every cadence gets —
daily jobs at 3 days (unchanged), weekly jobs at 9.5 (6.5 due + 3).
`JobDeadMan` (in `Kbo.Jobs`) is the single source for the weekly job set
and the thresholds; `PulseCommand` resolves its weekly registrations from
it so registry and thresholds cannot diverge. `kbo doctor` and the
dashboard's job-health tiles both consult it. Capture-drop staleness and
the agent last-seen tiles keep the flat 3-day rule — they track daily
liveness, not job cadence.

## Consequences

A healthy weekly job is never flagged; a genuinely dead one is flagged 3
days after it should have rerun, matching the daily jobs' grace. Refines
G2-12's flat rule. A new weekly job must be added to `JobDeadMan`'s set —
enforced by proximity (PulseCommand takes its cadence from that set).
