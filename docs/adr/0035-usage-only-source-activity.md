# 0035 — Source activity is usage, not writes

Status: accepted · Date: 2026-08-15

## Context

ADR-0034 defined a source's activity as its newest silver event (any type).
Live verification immediately falsified that definition: a fleet-wide
legislator run on 2026-08-05 wrote `docs/ai/manifest.json` into every
repo, and that single machine-generated `knowledge.written` event kept
`repo-CareerPlatform` (no real work since 2026-07-15) and
`repo-RKruiter_TestClient` "active" — leaving their 77 dead rows on the
worklist. Candidate fixes considered: exclude machine-managed paths
(`docs/ai/**`) in gold code (hardcodes a legislator convention, against
the no-hardcoded-paths principle), make the exclusion registry-config
(most machinery for one case), or redefine activity as usage.

## Decision

Activity, for dormancy, counts only *usage* events: `knowledge.read` and
`context.loaded`. Writes never prove a source is alive on their own. Real
resumed work always emits context/read events through the capture hooks,
so a live project cannot be misclassified dormant; a maintenance stamp
that writes without reading no longer wakes a source. Refines the
activity definition in ADR-0034 (which otherwise stands).

## Consequences

Dormant sources' "Last activity" now means last *usage* — a source whose
only events are writes shows "never". No path conventions enter gold
code. Edge accepted: a session that only writes into a source's docs and
reads nothing there does not count as activity for it; if such a workflow
ever becomes real, revisit with event-origin classification.
