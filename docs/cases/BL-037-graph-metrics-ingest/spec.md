# BL-037 — graph-metrics ingest: registry pointer + pulse job

**Spec type: feature**
**Tier: 1 (light — EARS spec + hurting case; design fixed by the five operator
answers of the 2026-08-28 grill session)**

- **Branch:** `bl/037-graph-metrics-ingest`
- **Open:** 2026-08-28

## Intent

The second and third kbo-side obligations of the kbl pull contract (kbl
ADR-0006, kbo ADR-0042; sequencing checklist items 2–3): the kbo registry
carries the artifact pointer on the aggregated source's row, and a daily
pulse-style ingest job reads kbl's export artifact, validates each line, and
appends `graph.metrics/1` events to bronze through the internal path — under
dead-man coverage, shipping **before** kbl's first emit. Together with BL-036
(schema, merged) this completes the consumer that the emit order demands.

## Boundary

**In scope:** `KnowledgeSource.MetricsArtifact` optional field (registry
format, additive) + parser validation + `kbo registry show` display + the
sanitized `registry/` example; a public bronze scan for existing
`graph.metrics` dedup keys; `IngestGraphMetricsJob` (daily cadence, job name
`ingest-graph-metrics`) — envelope build per BL-036 job-event mechanics,
`EventValidator` validation, date+source dedup at ingress, quiet skip on
absent artifact, loud all-or-nothing failure on a present-but-invalid one;
pulse wiring in `PulseCommand`; tests at every boundary; OKF sync; CHANGELOG
`[Unreleased]`; backlog closure of the two kbl-contract checkboxes.

**Out of scope:** gold mirror tiles from `graph.metrics` (backlog:
"Gold mirror tiles"); `knowledge.written/2` `linkcount`; all kbl-side work
(kb-graph emitter, artifact producer, contract-check); dedup beyond
`date`+`source`; monitoring of kbl's own emit cadence (dead-man watches
kbo's job, not the sibling); any mutation of past bronze lines; recovery
tooling for partial ingests (all-or-nothing makes them impossible).

## Requirements

- **R-001** — ubiquitous: `KnowledgeSource` SHALL carry an optional
  `metricsArtifact` file path; a registry row without it parses exactly as
  before.
- **R-002** — WHEN a registry row carries `metricsArtifact`, THEN the parser
  SHALL reject the row unless the value is an absolute file path on a
  non-glob root.
- **R-003** — ubiquitous: the job `ingest-graph-metrics` SHALL run daily
  under pulse and its standard `job.completed`/`job.failed` mechanism, giving
  it dead-man coverage like every other pulse job.
- **R-004** — WHEN no registered source carries `metricsArtifact`, or the
  pointed-to artifact file does not exist, THEN the job SHALL complete
  quietly with a summary stating what is absent.
- **R-005** — WHEN the artifact exists, THEN the job SHALL read each NDJSON
  line as a `graph.metrics/1` data payload, build the envelope per the BL-036
  job-event mechanics (`agent: "kbo"`, `subject` and `kbroot` = the carrying
  row's source id), reject a payload whose `source` does not equal the
  carrying row's id (pointer skew is loud), and validate every built event
  through `EventValidator`.
- **R-006** — WHEN any artifact line fails to parse or any built event fails
  validation, THEN the job SHALL fail loudly (exception → `job.failed`) with
  zero appends from that run — all-or-nothing per run.
- **R-007** — ubiquitous: before appending, the job SHALL scan bronze for
  existing `graph.metrics` events and skip artifact lines whose `data.date` +
  `data.source` key is already present — one bronze line per snapshot, ever.
- **R-008** — WHEN an existing artifact yields only duplicate lines, THEN the
  job SHALL complete with a summary reporting zero new events and the count
  skipped.

## Hurting case (the one it must never break)

**GIVEN** kbl's artifact exists with one snapshot line (`date` D, `source`
`knowledge`) and bronze already holds a `graph.metrics` event with the same
date+source key,
**WHEN** pulse runs `ingest-graph-metrics`,
**THEN** no new bronze line is appended — the mirror tile can never
double-count a snapshot,
**AND** `job.completed` carries a zero-new summary with the skip count,
**AND** the same run with the artifact deleted completes quietly with the
absent summary (dead-man still sees a live job),
**AND** the same artifact with one line violating `graph.metrics/1` (or
naming a foreign `source`) turns the run into `job.failed` with zero
appends.

## Clarifications (grill session 2026-08-28)

1. Registry pointer → optional field on the source row (`metricsArtifact`),
   not a separate registry section. (operator, recommended accepted)
2. Card-graph entry → the field goes on the **existing** `knowledge` row
   (`global`, `/home/admin/Knowledge`) — the graph aggregates the vault; no
   new row, no Root question. (operator, recommended accepted)
3. Idempotency point → ingress in the job: bronze scanned for the
   date+source key before append; one bronze line per snapshot ever.
   (operator, recommended accepted)
4. Artifact content → NDJSON of `data` payloads (the ten `graph.metrics/1`
   fields, including `contract_version`); kbo builds the envelope per BL-036
   job-event mechanics; kbl never authors envelopes. (operator, recommended
   accepted)
5. Missing artifact → quiet skip (`job.completed`, absent summary);
   present-but-invalid → loud `job.failed`. (operator, recommended accepted)

Mechanical/code-fact decisions at spec time (record, not operator-level):

- Job cadence daily — matches the snapshot-date semantics of BL-036
  clarification 3 (`date` = snapshot of the daily job; same-day re-runs hit
  the same dedup key).
- Dedup scan follows the `BronzeStore` scan precedent
  (`LastCompletedJobs`): a public keys method over the same
  poison-tolerant line loop.
- The job appends through `BronzeStore` (the internal path, ADR-0030 lock
  protocol); `PulseRunner` keeps wrapping it in `job.completed`/`job.failed`
  like every pulse job.
- Deployment note (operator act, not code): the real
  `~/.config/kbo/registry.yaml` gains `metricsArtifact` on `knowledge` when
  kbl's emit approaches — until then R-004's quiet skip is the steady state.
- No new ADR: every invariant invoked is already fixed by kbo ADR-0042,
  kbl ADR-0006 (touchpoint 4: "the pointer to it lives in the kbo registry
  entry"), and the registry's additive-evolution precedent (ADR-0036
  `excludePaths`).

## Plan

- **T1** `[P]` per R-001, R-002 — `KnowledgeSource.MetricsArtifact` +
  `SourceEntry`/parser validation (absolute path; reject on glob root) +
  `kbo registry show` display + sanitized `registry/` example row.
- **T2** `[P]` per R-007 — public `BronzeStore` scan returning the set of
  existing `graph.metrics` date+source keys, over the shared
  poison-tolerant `ReadEvents` loop.
- **T3** per R-003, R-004, R-005, R-006, R-007, R-008 —
  `IngestGraphMetricsJob` (payload parse, envelope build, source-skew
  rejection, validator pass, dedup, summaries) + `PulseCommand` wiring +
  tests for every leg of the hurting case.
- **T4** per R-001, R-002, R-003, R-004, R-005, R-006, R-007, R-008 — OKF
  sync (registry doc, glossary row, log), CHANGELOG `[Unreleased]`, backlog
  checkbox closure, journal.

## Converge (2026-08-28)

Audited against every R-line and the hurting case, code over diff:

- R-001: `KnowledgeSource.MetricsArtifact` (nullable init); rows without it
  parse unchanged — proven by the defaults test plus every pre-existing
  registry test staying green.
- R-002: relative value and glob-root row both rejected naming the source —
  `Parse_RelativeMetricsArtifact_ThrowsNamingThePath`,
  `Parse_MetricsArtifactOnGlobRoot_Throws`.
- R-003: `Name`/`Cadence` asserted; `Run_UnderPulseRunner_WrappedInJobCompleted`
  proves the `job.completed` wrap (dead-man sight); `PulseCommand` registers
  the job between harvest and rebuild so today's snapshot reaches today's
  silver; `JobDeadMan` untouched — daily is the default cadence.
- R-004: both quiet-skip legs tested (no pointer; absent file) with the
  absent summary naming the source.
- R-005: envelope fields asserted (`schemaref` `graph.metrics/1`, agent
  `kbo`, `subject`=`kbroot`=row id); foreign-`source` payload throws with
  the mismatch message; validation through `EventValidator` is exercised by
  the valid line passing and the broken line failing.
- R-006: `orphans: -3` and a non-JSON line each throw with **zero** bronze
  appends — pending is flushed only after the whole artifact parsed and
  validated.
- R-007: re-ingest appends nothing, reports skip count, bronze holds exactly
  one line; intra-run dupes caught by seeding the seen-set from
  `BronzeStore.GraphMetricsKeys()`.
- R-008: the all-duplicate run summarizes `ingested 0 … skipped 1 duplicate`.
- Hurting case: all four legs green (`Run_ReIngest_SkipsDuplicatesWithZeroNewSummary`,
  `Run_AbsentArtifact_SkipsQuietly`, `Run_InvalidMetric_ThrowsAndAppendsNothing`,
  `Run_ForeignSource_ThrowsAndAppendsNothing`).
- Out-of-scope respected: git status touches only kbo files — no gold/tile
  code, no `knowledge.written/2`, kbl tree untouched (read-only reference).
- Docs: registry.md (shape, behavior bullet), pulse.md (job table),
  glossary `metrics artifact`, log entry, CHANGELOG `Added`, backlog
  checkboxes closed with the operator-act note, journal.
- Gates: build 0 warnings / 0 errors; 318/318 tests (+13); `sdd-lint`,
  `anchors`, `okf-debt` all rc=0.

✅ Converged
