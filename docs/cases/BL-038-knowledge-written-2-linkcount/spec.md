# BL-038 — `knowledge.written/2`: the `linkcount` field

**Spec type: feature**
**Tier: 1 (light — EARS spec + hurting case + clarify; field semantics fixed
by the grill answers of the 2026-08-29 session)**

- **Branch:** `bl/038-knowledge-written-2-linkcount`
- **Open:** 2026-08-29

## Intent

The next open kbo-side obligation of the kbl pull contract (backlog "kbl
contract" section; kbl glossary: *linkcount = out-links of a written card,
the measurable discipline metric*). The live capture path learns to count
the wikilinks of a note at the moment it is written, and the event schema
grows an additive `linkcount` field as `knowledge.written/2` — the first
version bump this registry has ever performed, exercising the ADR-0002
evolution path (additive-only, no upcaster needed, v1 bronze lines stay
valid as v1 forever).

## Boundary

**In scope:** `schemas/knowledge.written/2.json`; golden fixture
`schemas/golden/knowledge.written.2.ndjson`; one broken fixture proving the
gate rejects a bad `linkcount`; shared wikilink counting (pure function) +
its wiring into both live adapters (Claude Code, opencode); harvest miners
stamp v2 with `linkcount` null; `EventEnvelope` gains the ability to stamp a
non-1 schema version; taxonomy row update in `docs/events.md`; OKF sync
(schema-registry.md, claude-code-adapter.md, opencode-adapter.md, harvest.md,
glossary, log); CHANGELOG `[Unreleased]`; backlog closure.

**Out of scope:** gold mirror tiles that *consume* `linkcount` (separate
backlog item, "Gold mirror tiles"); any kbl-side change (kb-graph computes
the authoritative card graph — `linkcount` is kbo's cheap write-time signal,
not a second graph engine); `contenthash` on opencode live writes (observed
asymmetry, but changing it is unrequested here); standard-markdown
`[text](path)` link counting (wikilinks only — kbl's link kind); anchor /
alias resolution beyond stripping (no path resolution, no existence checks
on targets); silver schema changes (data lands as JSON; views stay as-is
until a consumer asks).

## Requirements

- **R-001** — ubiquitous: the registry SHALL contain
  `schemas/knowledge.written/2.json`, composing `envelope/1`, with `type`
  const `knowledge.written`, `schemaref` const `knowledge.written/2`, and
  exactly v1's fields plus the optional nullable `data.linkcount`
  (integer ≥ 0) — additive-only, nothing else changes (ADR-0002).
- **R-002** — WHEN the live capture path (either adapter) maps a file-tool
  write whose subject resolves to a registered knowledge root (`kbroot` !=
  null) and whose content kind is knowledge (`ContentKind.Of(path)` ==
  `knowledge`) and the file exists on disk within the 5 MB hash cap, THEN
  the emitted event's `data.linkcount` SHALL carry the count of distinct
  wikilink targets in the file body as it lies after the write.
- **R-003** — WHEN any R-002 precondition fails (unregistered root,
  non-knowledge content kind, file missing, over cap), THEN the event SHALL
  still be emitted as `knowledge.written/2` with `data.linkcount` null.
- **R-004** — WHILE counting, the counter SHALL reduce the note body to a
  distinct normalized target set — alias part (`target|alias`) and anchor
  part (`target#anchor`, bare `#heading`) stripped, duplicates counted
  once, embeds (`![[target]]`) counted as references — with `linkcount` as
  that set's size.
- **R-005** — WHEN harvest mines a write from transcripts, THEN the miner
  SHALL emit `knowledge.written/2` with `data.linkcount` null (the note on
  disk may have moved on; harvest never re-reads subjects — same stance as
  `contenthash`, G2-5 / ADR-0030).
- **R-006** — ubiquitous: the golden corpus SHALL cover
  `knowledge.written/2` with at least two synthetic events — one live-shaped
  with a numeric `linkcount`, one with `linkcount` null — every line of
  which validates.
- **R-007** — WHEN a `knowledge.written/2` event carries `linkcount` as a
  negative integer, a non-integer, or a string, THEN validation SHALL fail
  (proven by a broken fixture the CI gate rejects).
- **R-008** — ubiquitous: existing `knowledge.written/1` bronze lines SHALL
  remain valid exactly as they lie: the v1 schema file stays in the
  registry, v1 golden fixtures keep validating, and no upcaster is added
  (additive evolution lifts nothing — silver reads `data` fields by JSON
  path, absent fields are simply null).

## Hurting case (the one it must never break)

**GIVEN** an agent writes a vault note whose body contains `[[Alpha]]`,
`[[Alpha|shown differently]]`, `[[Beta#section]]`, and `![[Gamma]]`,
**WHEN** the live hook emits the `knowledge.written` event,
**THEN** `schemaref` is `knowledge.written/2` and `data.linkcount` == 3
(distinct normalized targets: Alpha, Beta, Gamma),
**AND** the same session's harvest-mined copy of that write validates with
`linkcount` null,
**AND** every `knowledge.written/1` line already in bronze (golden corpus
included) still validates against the v1 schema — the bump rewrites no
history and breaks no reader.

## Clarifications (grill session 2026-08-29)

1. Counting rule → distinct normalized targets: strip alias (`|`) and anchor
   (`#`), dedup; embeds `![[X]]` count as references. Matches kbl's
   edge-between-cards definition. (operator, recommended accepted)
2. Gating → knowledge notes only: `kbroot` != null AND knowledge content
   kind AND file exists AND ≤ 5 MB cap; otherwise `linkcount` null, event
   still v2. Mirrors the `contenthash` gating. (operator, recommended
   accepted)
3. Harvest → miners stamp `knowledge.written/2` with `linkcount` null — one
   event population; precedent is `contenthash` null on harvest (G2-5).
   (operator, recommended accepted)
4. Opencode symmetry → both live adapters compute `linkcount` through the
   shared counter from day one; the observed missing `contenthash` on
   opencode writes is a separate observation (journal), not this case.
   (operator, recommended accepted)

## Mechanical/code-fact decisions at spec time (record, not operator-level)

- `EventTypes` gains a typed `knowledge.written/2` ref so no adapter
  hardcodes a version string; `EventEnvelope.Create` accepts an optional
  schema-version override defaulting to 1 — all other types unchanged.
- The counter is a pure static (regex + distinct set), shared by both
  adapters; it lives with the adapters (`Kbo.Adapters`), takes the file
  content string, and does no I/O — the adapters already read the file for
  hashing, one read serves both (hash + count on the same bytes).
- No ADR: every invariant invoked is already fixed by ADR-0002 (evolution
  path), ADR-0001 (envelope), ADR-0030 (content discipline). The first-ever
  version bump is *execution* of ADR-0002, not a new decision.
- Silver needs no migration: `events` stores `data` as JSON and every
  consumer extracts by path; `linkcount` flows to future tiles untouched.

## Converge (2026-08-29)

Audited against every R-line and the hurting case, code over diff:

- R-001: `schemas/knowledge.written/2.json` = v1 plus the nullable
  `linkcount` property (integer ≥ 0) and the version consts; diff against
  `1.json` shows nothing else moved. Auto-embedded via the csproj wildcard —
  `Every_schema_version_has_golden_coverage` enumerates it.
- R-002: both live adapters' write branches (Claude Code Write/Edit/
  NotebookEdit, opencode write/edit) call the shared `Wikilinks.CountDistinct`
  under the gating; proven by the hurting-case test (body of four link
  variants → `linkcount` 3) and the opencode leg (five variants → 4).
- R-003: null legs proven — code file under a registered root (linkcount
  null while `contenthash` present), file missing (opencode write to a
  non-existent path), write outside any kbroot; every leg still stamps
  `knowledge.written/2` and validates.
- R-004: `WikilinksTests` covers alias, anchor, bare-anchor self-reference,
  dedup, embeds, whitespace trim, and empty/plain bodies.
- R-005: both miners stamp v2 with `linkcount` null; proven at unit level
  (Claude miner) and end-to-end into bronze for both agents
  (`HarvestCommandTests` asserts the v2 schemaref and `"linkcount":null` in
  the appended lines).
- R-006: `schemas/golden/knowledge.written.2.ndjson` — one live-shaped
  event (`linkcount` 3, `origin: hook`) and one harvest-shaped
  (`linkcount` null, `origin: harvest`, `agent: opencode`); both validate;
  coverage test carries `per R-006`.
- R-007: `fixtures/broken/bad-linkcount.ndjson` (`linkcount: -3`) rejected
  by `Every_broken_fixture_fails_validation`.
- R-008: `schemas/knowledge.written/1.json` byte-identical, v1 golden lines
  still validate (`per R-008` on the corpus gate), no `upcasters/` code
  added anywhere.
- Hurting case: all three legs verified above (live count 3, harvest null
  copy validates, v1 history intact).
- Out-of-scope respected: git diff touches no `Gold/`, no `Silver/`, no
  registry code, no adapters' hook scripts; kbl tree untouched.
- Gates: build 0 warnings / 0 errors, 330/330 tests (was 321 — 5 counter
  unit, 2 live-adapter, 1 opencode e2e, 3 auto-adopted fixture lines),
  `sdd-lint` rc=0, `anchors` rc=0, `okf-debt` rc=0, baseline regenerated
  with BL-038 rows (the colliding-R-id display between BL-037 and BL-038 is
  the register's documented ambiguity, not a defect).

✅ Converged
