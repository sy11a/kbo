# 0012. Dashboard: rendered by report, embedded chart specs, CDN Vega with SRI, status never color-alone

## Status

Accepted (executor decisions within `07 - Feature Specs` §Dashboard, Q7, G2-12, P2/P5; plan step 2.4; owner-confirmed items marked)

## Context

The spec fixes the surface (static HTML, charts as owner-editable `.vl.json`, first chart set named, dead-man tiles at the 3-day threshold, prominent "generated at"). Left open: who renders it, how specs travel, where the JS libraries come from, and tile semantics.

## Decision

1. **`kbo report` renders the dashboard** (architecture: "computes gold facts once; renders Markdown reports + HTML dashboard") — one gold moment, two surfaces. Outputs beside the report: `kbo-dashboard.html` + `kbo-dashboard.gold.json` in `_generated/`.
2. **Chart specs are embedded at build** (`charts/*.vl.json`, same pattern as schemas): the owner edits the spec in the repo, `dotnet publish` ships it. Live-editing without republish was traded away for a dependency-free binary; revisit if spec-tweaking becomes a ritual activity.
3. **Vega/Vega-Lite/vega-embed from CDN** (*owner-confirmed 2026-08-12*), version-pinned with computed SRI hashes (`integrity` + `crossorigin`) so a CDN compromise cannot inject script. All chart data is inlined — nothing about the KB leaves the machine; only the generic libraries are fetched.
4. **Health tiles**: dead-man per machine × agent × job from `job.completed` (status `red` past 3 days, G2-12) and last-seen per machine × agent from any event (same threshold). Status is computed in gold and shipped as a field; the renderer maps it to **text + symbol + color** (`✓ ok` / `✗ SILENT`) — never color alone.
5. **Charts follow the dataviz discipline**: categorical layer palette validated by the six-checks script (blue/orange/aqua/yellow, fixed order, direct end-of-line labels covering the contrast warning); rate charts are single-hue with the axis pinned 0–100%; the tokens chart is **two aligned panels, one axis each** — never dual-axis despite the 1000× scale gap.
6. **Failed-search denominators count only known hit counts** (`hits` non-null); with silver preferring harvest values (G2-6), hook-era nulls don't dilute the rate.

## Consequences

The dashboard reads `kbo-dashboard.gold.json` — the future judge or any consumer gets the same numbers (P2). Verified in a real browser: renders with zero console errors on live data. The first real signals surfaced immediately: failed-search rate oscillating 20–60% (a discoverability worklist candidate) and the cache-read vs fresh-token reuse trend. When machine #2 enrolls (2.5), the tile grids grow rows with no code change — the group-bys already carry machine.
