# BL-036 — `graph.metrics/1` schema + golden fixture

**Spec type: feature**
**Tier: 1 (light — EARS spec + hurting case; field set fixed by the operator
answers of the 2026-08-28 plan session)**

- **Branch:** `bl/036-graph-metrics-schema`
- **Open:** 2026-08-28

## Intent

The first kbo-side obligation of the kbl pull contract (kbl ADR-0006, kbo
ADR-0042): the corpus-aggregate event type `graph.metrics` ships in the kbo
schema registry — schema, golden fixture, validator embedding — **before**
kbl's kb-graph first emit. This spec is the joint Wave-0 field contract the
kbl emitter will conform to ("Fields are fixed in the Wave-0 spec" —
`kbl docs/okf/kbo-contract.md` touchpoint 2).

## Boundary

**In scope:** `schemas/graph.metrics/1.json`; golden fixture
`schemas/golden/graph.metrics.1.ndjson`; one broken fixture proving the gate
rejects; taxonomy row in `docs/events.md`; OKF sync (schema-registry.md,
glossary, log); CHANGELOG `[Unreleased]`; backlog row closure.

**Out of scope:** the ingest job (pulse-style, dead-man coverage — separate
case); the registry entry for the card-graph source and any registry-format
extension (separate case, needs its own grill); `knowledge.written/2`
`linkcount`; gold mirror tiles; kbl-side work (kb-graph, contract-check,
export artifact format beyond the fields this schema consumes); C# code —
embedding, validation, and golden coverage are all driven by existing
wildcards and generic tests, so this case adds no source.

## Requirements

- **R-001** (ubiquitous). The registry SHALL contain
  `schemas/graph.metrics/1.json`, composing `envelope/1`, with `type` const
  `graph.metrics` and `schemaref` const `graph.metrics/1`.
- **R-002** WHEN a `graph.metrics/1` event is validated, THEN its `data`
  SHALL require exactly: `origin` (const `job`), `date` (`YYYY-MM-DD`
  snapshot date), `source` (registry source id of the aggregated graph),
  `notes`, `orphans`, `links`, `linkrot` (integers ≥ 0), `indegree`
  (histogram object: in-degree → note count), `new_links_7d` (integer ≥ 0),
  `contract_version` (integer ≥ 1).
- **R-003** (ubiquitous). `orphans` SHALL count island notes — zero in-links
  AND zero out-links — so the weeding lens (kbl Wave 3, unreachability) and
  the tile read the same definition.
- **R-004** (ubiquitous). The idempotency dedup key (kbl ADR-0006 invariant
  4) SHALL be derivable from the event itself as `data.date` +
  `data.source`, without parsing `subject` semantics; enforcement lands with
  the ingest-job case.
- **R-005** (ubiquitous). The golden corpus SHALL cover `graph.metrics/1`
  with at least one synthetic event, every line of which validates.
- **R-006** WHEN a `graph.metrics/1` event carries a metric count below zero,
  THEN validation SHALL fail (proven by a broken fixture that the CI gate
  rejects).

## Hurting case (the one it must never break)

**GIVEN** kbl's kb-graph emits its first artifact and the (future) ingest job
appends a `graph.metrics` event into bronze,
**WHEN** `EventValidator.Validate` runs on that line,
**THEN** it returns valid for a well-formed aggregate (the golden fixture is
the frozen proof shape),
**AND** the same line with `orphans: -3` is rejected by the registry gate
(the broken fixture is the frozen proof of rejection),
**AND** re-ingesting the same `date`+`source` snapshot is detectable as a
duplicate from the event fields alone (R-004) — a re-run must never
double-count a tile.

## Clarifications (plan session 2026-08-28)

1. Orphan definition → island: zero in-links AND zero out-links. (operator,
   recommended accepted)
2. In-degree distribution → histogram map (`{"0": n, "1": n, …}`), not
   quantiles — flexible when tiles evolve. (operator, recommended accepted)
3. `date` semantics → snapshot date of the aggregate (daily job); a re-run
   the same day yields the same dedup key. (operator, recommended accepted)
4. Explicit `data.source` → yes, duplicated from `subject` on purpose: dedup
   logic must not parse `subject` semantics. (operator, recommended
   accepted)

Mechanical/code-fact decisions at spec time (record, not operator-level):

- Envelope fill for the golden fixture follows `job.completed` (self-emitted
  by kbo): `source: //<machine>/kbo`, `agent: "kbo"`,
  `session`/`repo`/`task`/`model` null; `subject` and `kbroot` both carry
  the registry source id so aggregate events join the first-class
  registered-root population (G2-1).
- `contract_version` mirrors the kbl export artifact's contract-version
  field (touchpoint 4) into bronze, so skew stays visible at analysis time,
  not only at ingest.
- No `raw` field: the event is kbo-native on the ingest path (same stance as
  `job.completed`/`job.failed`).
- Taxonomy justification (`docs/events.md` rule): the orphan/link-rot/density
  mirror tiles (kbo backlog, "Gold mirror tiles") are the demanding report
  questions; `notes`/`links` are the rate denominators those tiles need.
- No new ADR: every invariant invoked here is already fixed by kbo ADR-0042,
  kbl ADR-0006, and the schema-evolution path of ADR-0002.

## Converge (2026-08-28)

Audited against every R-line and the hurting case, code over diff:

- R-001: `schemas/graph.metrics/1.json` exists, composes `envelope/1` via
  `$ref`, carries both consts; `EventValidator.KnownSchemaRefs` picks it up
  from the embedded wildcard (proven by the coverage test enumerating it).
- R-002: `data.required` lists exactly the ten fields with the spec'd
  constraints; proven behaviorally — both golden lines validate, and the
  registry rejects on constraint violations.
- R-003: the island definition is normative in the schema description, the
  taxonomy row, and the glossary; computation itself is the emitter's
  (kbl kb-graph) and the ingest job's boundary — out of scope here, as
  specced.
- R-004: `date` + `source` are required, self-contained data fields; dedup
  needs no `subject` parsing. Enforcement lands with the ingest-job case,
  as specced.
- R-005: `schemas/golden/graph.metrics.1.ndjson` (2 events, same source,
  two dates — the dedup-key exercise); `Every_golden_event_validates` green
  on both lines, `Every_schema_version_has_golden_coverage` green.
- R-006: `fixtures/broken/bad-metric-count.ndjson` (`orphans: -3`)
  rejected — `Every_broken_fixture_fails_validation` green.
- Hurting case: valid shape frozen in golden, rejection frozen in broken,
  dedup derivability = R-004. All three legs verified.
- Out-of-scope respected: zero C# source changes (git diff touches only
  schemas, fixtures, and docs); kbl tree untouched.
- Gates: build 0 warnings / 0 errors, 305/305 tests (was 302 — the corpus
  gates auto-adopted the new fixtures), `sdd-lint` rc=0, `anchors` rc=0,
  `okf-debt` rc=0.

✅ Converged
