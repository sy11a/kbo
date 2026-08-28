---
type: Component
title: Dashboard — practice mirror + trust surfaces (the see surface)
description: kbo report renders a static HTML dashboard from gold — the calibrated practice mirror (six emoji-state tiles judged against their own history corridors, BL-033), a compact dead-man strip (cadence-aware thresholds), last-seen collapsed into details, the SDD practice panel, reuse/never-read-theme and write→read-loop worktables, failed-search + tokens charts — practice-first per ADR-0042: every section feeds a mirror tile or system trust.
tags: [component, dashboard, health, vega-lite, gold, sdd, mirror]
timestamp: 2026-08-28T00:00:00Z
status: implemented
---

# Dashboard (`kbo report` → `_generated/kbo-dashboard.html`)

The *see* surface (Q7): trends and health tiles; never worklists (the report owns *act*). All numbers come from gold (`kbo-dashboard.gold.json`) — the HTML renderer injects them into charts and tiles, zero computation (P2). Implementation decisions: ADR-0012. Practice-first (ADR-0042): every section feeds a mirror tile or system trust — a section that answers no mirror question is cut from RENDER. Section order (BL-035): practice mirror → dead-man strip → last seen (collapsed) → SDD panel → reuse + never-read themes → write→read loop → failed-search chart + top zero-hit → tokens trend → recent sessions.

## Health panel (P5)

- **Practice mirror** (2026-08-27 design session, calibrated in BL-033): the first screen — six tiles, each judged against its **own** weekly-snapshot history, not a-priori thresholds. State is an emoji, never a bare color: 🟢 stable-good (inside the corridor and the corridor inside the goal) · 🔴 stable-sick (corridor outside the goal — chronic, a backlog item, no alarm styling) · 📈/📉 sustained drift (OLS slope > 2×MAD of weekly first differences, ≥0.5pp/week) · ⚠️ acute — the **only** amber — when the live value breaks its norm at robust z > 2 (saturated series: >2pp absolute) · ⏳ placeholder while history < 6 weeks. Corridors are p25–p75 of the tile's history: weekly Monday-UTC snapshots of the same windowed metric (14d for cache/burner/failed-search, 42d for loop/single-use/SDD) over the trailing 8 completed weeks, recomputed by every report into gold. Cache-discipline and burner are **trust tiles** (no goal, trend suppressed — only an acute break alarms). Goal lines are static in v1 («цель ≤15% · до цели −14пп»); the ratchet is a deferred backlog item. Tiles: `CacheAt`/`BurnerAt`/`FailedAt`/`LoopAt`/`SingleUseAt`/`SddAt` windowed queries + `MirrorCalibration` (pure math: percentiles, MAD, robust z, OLS slope, the state machine) in `src/Kbo/Gold/`.
- **Practice vs service:** every usage lens counts *practice* sessions only; sessions launched as `opencode --agent service-*` are filtered via silver's `practice_events` view and disclosed as a one-line "Служебные сессии: N исключено" note under the header (ADR-0039; a note, not a section). Dead-man, last-seen, sessions tables see them as usual.
- **Dead-man strip** (BL-035, ADR-0037 thresholds): one line when every job is inside cadence — "Dead-man: 6/6 ok · oldest `<job>` `<days>`d / limit `<days>`d" (per-job cadence: 3d daily, 9.5d weekly); a red job restores its full tile (machine · agent · job, days silent) beside the strip. Green jobs are silence — a grid of green tiles is wallpaper.
- **Last-seen tiles** per machine × agent: newest bronze event of any type — collapsed inside a `<details>` element whose summary names the agent count and the newest event age (provenance detail, not a mirror question).
- **"generated at"** rendered prominently — a stale dashboard must look stale.
- **Live refresh (ADR-0022)**: `kbo watch [--interval <seconds>]` is a foreground loop that rebuilds silver and re-renders the dashboard each tick, emitting a `<meta http-equiv="refresh">` so an open tab self-reloads. No server or daemon (stops on Ctrl-C); each tick is still a compute-once render (P2). `report`/pulse render without the auto-reload tag.
- **Recent sessions** table: the last `RecentSessionCap` (30) sessions across all repos, newest first — per session: date/time, agent, repo, reads · searches · skills · writes, KB-touch, tokens. The session-level detail on the dashboard itself (the day pages carry the same per-session table scoped to each day).
- **Write → read loop** (ADR-0027): of the notes agents created/edited in the window, what fraction were later read — the knowledge flywheel; plus the top written-then-read notes.
- **Most-reused knowledge notes** (ADR-0026): notes (`.md` only) ranked by distinct-session reach over the 60-day window, plus the single-use ratio — the load-bearing core vs the single-use tail, the ritual's keep/promote/prune signal.
- **Never-read themes** (ADR-0017): themes (registered source id + first path segment under its root) with zero reads in the 60-day window, listed with note counts — ritual candidates. The theme aggregation query stays gold-side (`UnusedThemes`); the per-theme reads series and its chart were cut in BL-035.
- **Top zero-hit searches** ranked list (top `TopListCap` = 15 over the 60-day window): which search queries most often found nothing — each line a candidate for a new or renamed note.
- **SDD practice panel** (ADR-0040): the before/after instrument for the legislator's SDD law (edition v17). Three metrics over the 60-day window, practice sessions only: (1) **spec-before-code ordering** — share of code-writing sessions whose earliest spec activity (`/docs/superpowers/` or `/docs/cases/` subjects, read or write) strictly precedes their first code write (`ContentKind` code), per repo × ISO week + fleet summary; (2) **writes by content kind** — the docs-vs-code balance of `knowledge.written`, with machine-managed writes (`/docs/ai/` — constitution copies, the future generated baseline) excluded **and disclosed as a count** (no-silent-caps: machine writes are not documentation discipline); (3) **SDD-skill rate** — share of sessions invoking ≥1 skill from the registry's optional `sdd: { skills: [...] }` block; no block → the other two metrics render and the skill table states "not configured" (ADR-0031 pattern — a public tool ships no default skill names).

## Cut in BL-035 (2026-08-28, practice-first rule of ADR-0042)

Week-over-week section (ADR-0028 is historical; the computation died with it — mirror tiles compute their own windows), reads-over-time-by-layer chart + `ReadsByLayer`, reads-by-content-type list and its computation, reads-by-theme chart + the per-theme reads series, the top-skills list and its computation, sessions-by-repository table and its computation (registration candidates stay with `kbo audit completeness`), kb-touch chart + `KbTouch` daily series (KB-touch as a mirror question belongs to the owed metric-set brainstorm), constitution-fleet panel (ADR-0038 — the summary stays on the report stdout line + gold json), the standalone service-sessions section (the ADR-0039 disclosure line survives). Ad-hoc analysis of the cut series lives in silver (bronze is sufficient — rebuild re-derives everything).

## Charts (v2 set, spec-fixed)

| Spec (`charts/*.vl.json`, owner-editable, embedded at build) | Shows |
|---|---|
| `failed-search-rate.vl.json` | zero-hit share of knowledge searches — daily line + 7-day mean, green zone ≤ 10%, red zone ≥ 30% |
| `tokens-trend.vl.json` | cache-read vs fresh input tokens, two aligned panels (one axis each — never dual-axis) |

- Vega/Vega-Lite/vega-embed load from CDN (owner-confirmed); all data is inlined — nothing leaves the machine.
- Datasets come from silver's `events_preferred`/`sessions`. Knowledge classification is **registry-now** (ADR-0021): surviving read lenses (themes, reuse, loop, recent sessions) resolve event subjects through the current registry at report time, the same rule as before — capture-time `kbroot` stamps stay in bronze/silver as the historical record but no longer drive the surface.
- **Russian usage descriptions** (what the chart shows + where to look) live in each spec's `usermeta.kbo.ru` — owner-editable like the rest of the spec; the renderer HTML-encodes and prints them under the chart title. Every embedded spec must carry one (enforced by test). Tile sections carry renderer-owned Russian descriptions.
- **Green/red zones** are literal `rect` layers in the specs — thresholds are owner-tunable numbers, not code.

## Implementation

- `charts/*.vl.json` — the owner-editable specs (embedded like schemas; edit → republish)
- `src/Kbo/Gold/DashboardComputer.cs` + `DashboardGold` — every number born here
- `src/Kbo/Gold/MirrorCalibration.cs` — the mirror's calibration math + state machine (BL-033)
- `src/Kbo/Gold/DashboardRenderer.cs` — HTML, zero computation
- Rendered by `kbo report` alongside the worklists (architecture: report computes gold once, renders Markdown + dashboard)

## Links

- [Pulse](pulse.md) — emits the `job.*` events the tiles read · [Silver](silver.md) · [First report](first-report.md)
