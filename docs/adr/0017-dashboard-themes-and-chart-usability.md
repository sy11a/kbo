# 0017. Dashboard themes and chart usability

## Status

accepted

## Context

After the first week of use the owner asked four things of the dashboard: per-chart descriptions in Russian (what it shows, where to look), richer charts ("too low detalization — use some library"), a view of which parts of the knowledge base are used vs not (themes/topics), and visual green/red zones for good/bad. Two decisions needed an owner call: whether to swap the charting library, and what counts as a "theme".

## Decision

- **Stay on Vega-Lite, enrich the specs** (owner-confirmed over ECharts/Plotly): tooltips, visible points, x-axis zoom/pan, 7-day moving-average layers. No new dependencies; the owner-editable `charts/*.vl.json` workflow is preserved.
- **Theme = top-level folder under a registered root** (owner-confirmed over full paths / frontmatter tags): label `<source-id>/<first-segment>`, files directly in a root fall under the source id. Gold computes reads per theme over a 60-day window (`ThemeWindowDays`) plus note counts from the inventory, pre-split into `ThemeReads` (top 20, bar chart) and `UnusedThemes` (never-read list) — renderers stay computation-free (P2).
- **Green/red zones are literal `rect` layers in the specs**, thresholds owner-tunable by editing the JSON: KB-touch green ≥ 50% / red ≤ 20%; failed-search green ≤ 10% / red ≥ 30%. Zones only on the two rate charts, where an absolute good/bad exists.
- **Russian descriptions live in each spec's `usermeta.kbo.ru`** (owner-editable, HTML-encoded by the renderer); a test forces every embedded spec to carry one. Tile sections get renderer-owned Russian descriptions.

## Consequences

- The dashboard is self-explanatory for the owner without this repo's docs; descriptions travel with the specs they describe.
- Theme granularity follows vault folder structure — reorganizing folders changes the breakdown (accepted: the vault's top level is the owner's own taxonomy).
- Threshold tuning requires no rebuild logic knowledge, only editing two numbers in a spec — but a rebuild to re-embed (`dotnet publish`), same as any spec edit.
- Known benign console warning: Vega-Lite 5.21 logs `Dropping "fit-y" because spec has discrete height` for the container-width step-height bar chart; the compiled autosize is exactly the intended `fit-x` (verified by compiling the spec in-browser).
