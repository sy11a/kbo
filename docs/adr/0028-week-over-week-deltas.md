# 0028. Week-over-week deltas

## Status

accepted

## Context

The dashboard shows current state and 7-day averages, but nothing answers "is the practice improving?" directly. A trend needs an explicit comparison of this week to last week, colored by whether the movement is good or bad for each metric.

## Decision

Add a **"This week vs last week"** summary to the dashboard: `WeekOverWeek` (gold) computes three metrics over `[now-7d, now)` vs `[now-14d, now-7d)` — KB-touch rate (higher better), failed-search rate (lower better), knowledge-note reads count (higher better, notes-only via `ContentKind`). Each is a `MetricDelta` (label, current, previous, format, higher-is-better). The renderer shows current value, an arrow, the signed change (percentage points for rates, count for reads), and the prior value — colored green when the movement improves the practice and red when it worsens, using `higher-is-better` per metric so a rising failed-search rate reads red.

## Consequences

- The dashboard answers "is it getting better?" at a glance, with direction-correct coloring per metric.
- Uses the touched-session set (ADR-0021) and `ContentKind` (ADR-0025); no schema or capture change.
- Fixed 7-day windows anchored on report time; a partial current week (report run mid-week) compares fewer days against a full prior week — a known, acceptable skew for a weekly-cadence practice.
- Completes the four owner-selected practice lenses (content-type ADR-0025, reuse ADR-0026, write→read loop ADR-0027, deltas here).
