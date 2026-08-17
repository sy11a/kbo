---
type: Component
title: Dashboard — health panel + Vega-Lite charts (the see surface)
description: kbo report renders a static HTML dashboard from gold — dead-man health tiles (cadence-aware thresholds), a sessions-by-repository provenance table, owner-editable .vl.json charts with Russian descriptions and green/red zones, and a reads-by-theme breakdown with a never-read list.
tags: [component, dashboard, health, vega-lite, gold]
timestamp: 2026-08-16T00:00:00Z
status: implemented
---

# Dashboard (`kbo report` → `_generated/kbo-dashboard.html`)

The *see* surface (Q7): trends and health tiles; never worklists (the report owns *act*). All numbers come from gold (`kbo-dashboard.gold.json`) — the HTML renderer injects them into charts and tiles, zero computation (P2). Implementation decisions: ADR-0012.

## Health panel (P5)

- **Practice vs service:** every usage lens counts *practice* sessions only; sessions launched as `opencode --agent service-*` are filtered via silver's `practice_events` view and disclosed as a "Служебные сессии: N исключено" note (ADR-0039). Dead-man, last-seen, sessions tables see them as usual.
- **Dead-man tiles** per machine × agent × job: last `job.completed`, days silent, status `ok`/`red` at the job's cadence threshold (3d daily, 9.5d weekly — ADR-0037 refining G2-12). Status ships as text + symbol, never color alone.
- **Last-seen tiles** per machine × agent: newest bronze event of any type.
- **"generated at"** rendered prominently — a stale dashboard must look stale.
- **Live refresh (ADR-0022)**: `kbo watch [--interval <seconds>]` is a foreground loop that rebuilds silver and re-renders the dashboard each tick, emitting a `<meta http-equiv="refresh">` so an open tab self-reloads. No server or daemon (stops on Ctrl-C); each tick is still a compute-once render (P2). `report`/pulse render without the auto-reload tag.
- **Sessions by repository** table: full working-directory paths sessions were started from (the `repo` envelope field, populated by both adapters), session count, agents, and last-session date over the 60-day window (cap 50 rows) — the provenance map of where captured data comes from. A folder with many sessions but no knowledge reads is a registration candidate.
- **Recent sessions** table: the last `RecentSessionCap` (30) sessions across all repos, newest first — per session: date/time, agent, repo, reads · searches · skills · writes, KB-touch, tokens. The session-level detail on the dashboard itself (the day pages carry the same per-session table scoped to each day).
- **This week vs last week** (ADR-0028): KB-touch, failed-search, and knowledge-reads compared over the last 7 days vs the prior 7, with direction-correct green/red arrows — the "is the practice improving?" summary.
- **Write → read loop** (ADR-0027): of the notes agents created/edited in the window, what fraction were later read — the knowledge flywheel; plus the top written-then-read notes.
- **Most-reused knowledge notes** (ADR-0026): notes (`.md` only) ranked by distinct-session reach over the 60-day window, plus the single-use ratio — the load-bearing core vs the single-use tail, the ritual's keep/promote/prune signal.
- **Reads by content type** ranked breakdown (ADR-0025): registered reads split into knowledge (`.md`…), code, config, other over the 60-day window — shows what fraction of "knowledge reads" are actual notes vs source code swept in by whole-repo registration. Other read-metrics still count all registered reads; this makes the composition legible.
- **Top skills used** and **Top zero-hit searches** ranked lists (top `TopListCap` = 15 over the 60-day window): which skills (`skill.invoked`) agents leaned on, and which search queries most often found nothing — the day-page detail (skill names, query text) surfaced on the dashboard.

## Charts (v2 set, spec-fixed)

| Spec (`charts/*.vl.json`, owner-editable, embedded at build) | Shows |
|---|---|
| `reads-over-time.vl.json` | daily knowledge reads by registry layer (categorical palette, validated; direct end labels; points + x-zoom) |
| `reads-by-theme.vl.json` | most-read themes (top-level folder per registered root) over the 60-day window, horizontal bars capped at 20 |
| `kb-touch-rate.vl.json` | share of sessions touching registered knowledge — daily line + 7-day mean, green zone ≥ 50%, red zone ≤ 20% |
| `failed-search-rate.vl.json` | zero-hit share of knowledge searches — daily line + 7-day mean, green zone ≤ 10%, red zone ≥ 30% |
| `tokens-trend.vl.json` | cache-read vs fresh input tokens, two aligned panels (one axis each — never dual-axis) |

- Vega/Vega-Lite/vega-embed load from CDN (owner-confirmed); all data is inlined — nothing leaves the machine.
- Datasets come from silver's `events_preferred`/`sessions`. Knowledge classification is **registry-now** (ADR-0021): reads-by-layer and KB-touch resolve event subjects through the current registry at report time (with a still-registered-stamp fallback for searches), the same rule as themes — capture-time `kbroot` stamps stay in bronze/silver as the historical record but no longer drive the charts.
- **Russian usage descriptions** (what the chart shows + where to look) live in each spec's `usermeta.kbo.ru` — owner-editable like the rest of the spec; the renderer HTML-encodes and prints them under the chart title. Every embedded spec must carry one (enforced by test). Tile sections carry renderer-owned Russian descriptions.
- **Green/red zones** are literal `rect` layers in the specs — thresholds are owner-tunable numbers, not code.

## Reads by theme (ADR-0017)

- **Theme** = registered source id + first path segment under its root (`vault/rituals`); files directly in a root fall under the source id itself.
- Gold computes reads per theme over `ThemeWindowDays` (60) from `events_preferred` (`knowledge.read` + `context.loaded`), resolving every subject through the registry (so historical kbroot-null events still count), and note counts per theme from the inventory scan.
- Two pre-split lists (renderer computes nothing): `ThemeReads` (reads > 0, top `ThemeChartLimit` = 20, bar chart) and `UnusedThemes` (0 reads in window, listed under "Never-read themes" — ritual candidates).

## Implementation

- `charts/*.vl.json` — the owner-editable specs (embedded like schemas; edit → republish)
- `src/Kbo/Gold/DashboardComputer.cs` + `DashboardGold` — every number born here
- `src/Kbo/Gold/DashboardRenderer.cs` — HTML, zero computation
- Rendered by `kbo report` alongside the worklists (architecture: report computes gold once, renders Markdown + dashboard)

## Links

- [Pulse](pulse.md) — emits the `job.*` events the tiles read · [Silver](silver.md) · [First report](first-report.md)
