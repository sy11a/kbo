# kb-observability — Backlog

Tasks pending implementation. **Rule: update the relevant `docs/okf/` document first, before writing any code.** When a task is fully done, remove it from here.

---

## kbl contract (BL-001 in kbl) — kbo-side obligations

Fixed by kbl's ADR-0006 and its single law document (`~/Repository/kbl/docs/okf/kbo-contract.md`):
**kbo is the only writer of bronze**; kbl publishes an export artifact, kbo ingests (pull).
Order is critical: schema + golden fixture + consumer ride a kbo release **before** kb-graph's
first emit — capture silently drops unregistered schemarefs into the fail-safe log.

- [x] **`graph.metrics/1` schema + golden fixture** — the corpus-aggregate event (working
  name; field set belongs to the joint Wave-0 spec): orphans, link-rot, in-degree distribution,
  new-links-per-week; `origin: job`; dedup key date+source (idempotent re-ingest).
  ([BL-036](cases/BL-036-graph-metrics-schema/spec.md), converged 2026-08-28).
- [x] **Ingest job (pulse-style)** — reads kbl's export artifact via the registry-entry
  pointer, validates through `EventValidator`, appends to bronze through the internal path;
  under dead-man coverage. ([BL-037](cases/BL-037-graph-metrics-ingest/spec.md), converged 2026-08-28).
- [x] **Registry entry for the card graph (kbo-source)** — source row carrying the artifact
  path pointer (may need a registry-format extension — grill).
  Resolved by BL-037's grill: the optional `metricsArtifact` field on the existing
  aggregated source row; the machine-local registry gains the pointer when kbl's
  emit approaches (operator act, not code).
- [x] **`knowledge.written/2`** — `linkcount` field (out-links of the written note; the live
  hook computes it), schema evolution along the ADR-0002 path.
  ([BL-038](cases/BL-038-knowledge-written-2-linkcount/spec.md), converged 2026-08-29).
- [ ] **Gold mirror tiles** — orphan/link-rot/density trends from `graph.metrics`; fold into
  the owed "mirror v0.1 final metric set" brainstorm.
- [x] **ADR-0042** — the invariant "kbo is the only writer of bronze; sibling
  repos publish artifacts" + a cross-ref to kbl's `docs/okf/kbo-contract.md`
  ([ADR-0042](../adr/0042-kbo-canon-practice-first-dashboard.md), accepted 2026-08-28;
  link line landed in kbl's `docs/okf/architecture.md`).

## Register

| Case | What | Home |
|------|------|------|
| BL-033 | Mirror calibration v1 — emoji state model, p25–p75 corridors in gold json, trust-tiles, ⏳ placeholders (design session 2026-08-27; decisions + data checks inside) | [docs/cases/BL-033-mirror-calibration-v1/spec.md](cases/BL-033-mirror-calibration-v1/spec.md) |
| BL-035 | ADR-0042 (kbo's side of the canon) + dashboard declutter to the mirror questions — grill answers, cut list, strip/details/disclosure semantics | [docs/cases/BL-035-adr0042-declutter/spec.md](cases/BL-035-adr0042-declutter/spec.md) |
| BL-037 | graph-metrics ingest — registry `metricsArtifact` pointer on the aggregated source row + daily `ingest-graph-metrics` pulse job (dedup at ingress, quiet skip on absent artifact) | [docs/cases/BL-037-graph-metrics-ingest/spec.md](cases/BL-037-graph-metrics-ingest/spec.md) |
| BL-038 | `knowledge.written/2` — additive `linkcount` (distinct normalized wikilinks, live hooks only, harvest null); the registry's first version bump | [docs/cases/BL-038-knowledge-written-2-linkcount/spec.md](cases/BL-038-knowledge-written-2-linkcount/spec.md) |
| BL-001 (kbl) | kb-graph placement + kbo metrics pull-contract (kbl's ADR-0006); kbo-side obligations — the section above | `~/Repository/kbl/docs/cases/BL-001/readme.md` (case lives in kbl) |

## Deferred

- **Goal ratchet** (from BL-033 clarification 2): when a goal-bearing tile's corridor first
  sits fully inside its goal, re-anchor the goal to the best historical corridor edge.
  Implement on the first achievement, not before.
- **notify-send on acute ⚠️** (from BL-033): v1 is visual-only; add the notification when
  the first acute break is actually missed.
- **Per-model cache normalization** (from BL-033 clarification 3): cache-discipline corridor
  is global in v1; per-model medians become a lens when model mix starts shifting visibly.
- **~Sep-10 corridor recalibration** (calendar): first live render flags three ⚠️ acutes on
  the 42d-window tiles (z = 13 / 3 / 9.6) — overlapping windows give tight corridors;
  decide whether slow tiles need a wider diff-scale.
