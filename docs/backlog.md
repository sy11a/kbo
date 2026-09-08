# kb-observability — Backlog

Since 2026-09-08 this repository's backlog holds only the tasks of the release that is running — none today. The fleet's future work lives in Architector's tracker (Architector ADR 0009, case BL-011): the **idea backlog** (`Idea:` issues) and the **pre-release backlog** (`frozen` issues) at https://github.com/sy11a/Architector/issues; the operator places items from there into the next release. Nothing is filed here by hand.

## Moved to Architector (2026-09-08)

| Was here | Now |
|---|---|
| § kbl contract — Gold mirror tiles (orphan / link-rot / density trends from `graph.metrics`; the owed mirror v0.1 metric-set brainstorm) | [sy11a/Architector#37](https://github.com/sy11a/Architector/issues/37) — Idea: Card-graph metrics reach the practice mirror |
| § Deferred — goal ratchet, notify-send on acute ⚠️, per-model cache normalization, the ~Sep-10 corridor recalibration | [sy11a/Architector#59](https://github.com/sy11a/Architector/issues/59) — Mirror calibration v1 follow-ups (pre-release) |
| the entry-overhead backfill and the context axis wave 2 (filed in kbl's queue, land here — they write bronze) | [sy11a/Architector#46](https://github.com/sy11a/Architector/issues/46) — Idea: The context-cleanliness axis on kbo's mirror |

The kbl-contract obligations of the section that stood here are delivered — `graph.metrics/1` (BL-036), the ingest job (BL-037), the registry pointer, `knowledge.written/2` (BL-038), ADR-0042 — and their cases are in the register below.

## Register

| Case | What | Home |
|------|------|------|
| BL-033 | Mirror calibration v1 — emoji state model, p25–p75 corridors in gold json, trust-tiles, ⏳ placeholders (design session 2026-08-27; decisions + data checks inside) | [docs/cases/BL-033-mirror-calibration-v1/spec.md](cases/BL-033-mirror-calibration-v1/spec.md) |
| BL-035 | ADR-0042 (kbo's side of the canon) + dashboard declutter to the mirror questions — grill answers, cut list, strip/details/disclosure semantics | [docs/cases/BL-035-adr0042-declutter/spec.md](cases/BL-035-adr0042-declutter/spec.md) |
| BL-037 | graph-metrics ingest — registry `metricsArtifact` pointer on the aggregated source row + daily `ingest-graph-metrics` pulse job (dedup at ingress, quiet skip on absent artifact) | [docs/cases/BL-037-graph-metrics-ingest/spec.md](cases/BL-037-graph-metrics-ingest/spec.md) |
| BL-038 | `knowledge.written/2` — additive `linkcount` (distinct normalized wikilinks, live hooks only, harvest null); the registry's first version bump | [docs/cases/BL-038-knowledge-written-2-linkcount/spec.md](cases/BL-038-knowledge-written-2-linkcount/spec.md) |
| BL-001 (kbl) | kb-graph placement + kbo metrics pull-contract (kbl's ADR-0006); kbo-side obligations — the section above | `~/Repository/kbl/docs/cases/BL-001/readme.md` (case lives in kbl) |
