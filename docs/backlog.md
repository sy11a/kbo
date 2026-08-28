# kb-observability — Backlog

Tasks pending implementation. **Rule: update the relevant `docs/okf/` document first, before writing any code.** When a task is fully done, remove it from here.

---

## Dashboard declutter — answer only the mirror questions (2026-08-27 design session)

Rule: every section must feed a mirror tile or system trust. Session grill first (questions below).

**Cut from RENDER (8):** Week over week (duplicates the mirror's trends; KEEP the computation —
SearchTile feeds on it) · Reads-over-time by layer · Reads by content type · Reads by theme
chart (keep the query — UnusedThemes comes from it) · Top skills · Sessions by repo ·
Constitution fleet (the summary stays as a report line + gold json) · Service sessions.

**Cut from COMPUTER + DashboardGold + tests (dead computations, constitution "a field exists
only if a question requires it"):** ReadsByLayer · ReadsByContentType · TopSkills ·
SessionsByRepo · ServiceSessions.

**Keep & reorder to mirror-tile order:** Practice mirror → Dead-man (compact) →
Last seen collapsed into <details> → SDD panel → Reuse + Unused themes → Write→read loop →
Failed-search chart + Top zero-hit → Tokens-trend → Recent sessions.

**Grill before editing:** (1) amber-tile thresholds against 2 weeks of data — rebuild the
ranges? (2) WeekOverWeek section — reinstate as someone's drill-down, or dead forever?
(3) Dead-man — tile grid or a single strip line? (4) gold json: keep computing the cut
series for ad-hoc, or cut them per the constitution? (5) Report line: add the cut content
there (fleet is already there)?

## ADR-0042 — practice-first dashboard + kbo's place in the canon

Canon: **the legislator writes laws · kbl keeps the knowledge fund · kbo audits practice.**
kbo is a measuring instrument: (a) the mirror of six questions, (b) the system dead-man
(jobs + canary), (c) owner of the source registry — kbl registers in it as a source and
reads silver/gold strictly read-only. kbo never stores knowledge and never writes laws;
there is one measurement surface (kbl has no dashboard — its projections live in Obsidian).

**Grill:** (1) the exact wording of registry ownership; (2) fleet panel — just a report
line, or give the legislator repo its own summary? (3) include the canary in the ADR as
part of kbo's dead-man duty?

+ one link line to ADR-0042 in kbl/docs/okf/architecture.md (same session).

## kbl contract (BL-001 in kbl) — kbo-side obligations

Fixed by kbl's ADR-0006 and its single law document (`~/Repository/kbl/docs/okf/kbo-contract.md`):
**kbo is the only writer of bronze**; kbl publishes an export artifact, kbo ingests (pull).
Order is critical: schema + golden fixture + consumer ride a kbo release **before** kb-graph's
first emit — capture silently drops unregistered schemarefs into the fail-safe log.

- [ ] **`graph.metrics/1` schema + golden fixture** — the corpus-aggregate event (working
  name; field set belongs to the joint Wave-0 spec): orphans, link-rot, in-degree distribution,
  new-links-per-week; `origin: job`; dedup key date+source (idempotent re-ingest).
- [ ] **Ingest job (pulse-style)** — reads kbl's export artifact via the registry-entry
  pointer, validates through `EventValidator`, appends to bronze through the internal path;
  under dead-man coverage.
- [ ] **Registry entry for the card graph (kbo-source)** — source row carrying the artifact
  path pointer (may need a registry-format extension — grill).
- [ ] **`knowledge.written/2`** — `linkcount` field (out-links of the written note; the live
  hook computes it), schema evolution along the ADR-0002 path.
- [ ] **Gold mirror tiles** — orphan/link-rot/density trends from `graph.metrics`; fold into
  the owed "mirror v0.1 final metric set" brainstorm.
- [ ] **ADR-0042 (draft)** — add the invariant "kbo is the only writer of bronze; sibling
  repos publish artifacts" + a cross-ref to kbl's `docs/okf/kbo-contract.md`.

## Register

| Case | What | Home |
|------|------|------|
| BL-033 | Mirror calibration v1 — emoji state model, p25–p75 corridors in gold json, trust-tiles, ⏳ placeholders (design session 2026-08-27; decisions + data checks inside) | [docs/cases/BL-033-mirror-calibration-v1/spec.md](cases/BL-033-mirror-calibration-v1/spec.md) |
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
