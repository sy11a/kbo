# 0037 — Constitution-fleet panel from a manifest scan, not a registry

Status: accepted · Date: 2026-08-16

## Context

The AI-dev constitution (the legislator skill) is versioned centrally and
delivered per repo; after a version bump every legislated repo is stale
until re-legislated. Tracking that fleet needed a home. The candidate
designs were (a) a maintained database of legislated repos, updated on
each legislation, or (b) deriving the fleet at report time. A maintained
list is a second copy of state the repos already carry in
`docs/ai/manifest.json` — it drifts the moment a repo is moved, archived,
or deleted, and it is an artifact with no death condition.

## Decision

1. **No fleet registry.** The repos' `docs/ai/manifest.json` files are the
   database. `ConstitutionFleet.Scan` reads the constitution's VERSION
   file and scans the configured roots' **direct children** for manifests
   at report time (derived-rebuildable, same posture as the note
   inventory).
2. **Opt-in configuration** (ADR-0031 pattern): an optional top-level
   `constitution:` registry block — `versionFile` (absolute path to the
   legislator `skill/VERSION`) and `scanRoots` (absolute dirs whose direct
   children are candidate repos), plus optional `exclude` — directory
   basenames the scan skips (an archived repo keeps its manifest as
   history but is deliberately outside the fleet, and a permanently red
   row would violate the actionable-worklist law). No block → no panel; a
   public tool ships no default legislator location.
3. **Dashboard panel, no new job.** `kbo report`/`kbo watch` pass the scan
   into `DashboardGold` ("Constitution fleet — skill vN"): one row per
   repo, `ok` when its manifest version equals the current version, `red`
   otherwise. An unreadable manifest renders as version `?` and counts as
   behind — unknown classification fails toward the cheap error. A
   configured-but-missing `versionFile` fails the report loudly
   (`RegistryFormatException`), not silently without the panel.
4. **Delivery stays outside kbo**: the panel points at the legislator
   repo's `tools/fleet.sh upgrade`; kbo observes, it does not act.

## Consequences

- Fleet staleness is visible at the same place as job health, with zero
  bookkeeping: legislating a new repo or deleting an old one changes the
  panel on the next report, nothing to update.
- The scan is depth-1 by design (repos as direct children of scan roots);
  a repo nested deeper is invisible to the panel until its parent is added
  to `scanRoots` — the trade for not walking whole trees at report time.
- kbo now reads one file outside its stores (the legislator VERSION file)
  at gold time; it stays optional configuration, so the public tool is
  unaffected.
