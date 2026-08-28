# BL-035 — ADR-0042 + dashboard declutter

**Spec type: feature**
**Tier: 1 (light — EARS spec + hurting case; operator decisions from the
2026-08-27 design session + the 2026-08-28 grill session)**

- **Branch:** `bl/035-adr0042-declutter`
- **Open:** 2026-08-28

## Intent

Two units, one session. (1) **ADR-0042** — kbo's side of the canon: tenant-model
registry ownership, single writer of bronze, practice-first dashboard rule, canary
as dead-man duty without crossing ADR-0003's NOT-list. (2) **Dashboard declutter** —
the dashboard answers only the mirror questions (ADR-0042 §4): every section feeds
a mirror tile or system trust; sections and computations that answer no question die.

## Boundary

**In scope:** renderer section cuts and reorder to mirror-tile order; dead-man compact
strip; last-seen collapsed into `<details>`; the ADR-0039 service-sessions disclosure
surviving as one line; computer/gold cuts of the dead computations (ReadsByLayer,
KbTouch daily series, ThemeReads series field, SessionsByRepo, TopSkills,
ReadsByContentType, WeekOverWeek) with their record types; deletion of the three
orphaned chart specs; tests retargeted to the surviving surface; OKF dashboard.md +
log; CHANGELOG `Removed`.

**Out of scope:** mirror calibration changes (the ~Sep-10 recalibration owns
threshold/diff-scale work — deferred backlog line); daily digest pages (ADR-0023 — a
separate surface that keeps its own KB-touch and reads-by-layer day facts); the
constitution fleet scan (stays: stdout report line + gold json); Markdown worklists
and the report stdout line; kbl-side work; the "mirror v0.1 final metric set"
brainstorm (KB-touch as a future mirror tile belongs there, not here); gold json
`serviceSessions` and `constitutionFleet` fields.

## Requirements

- **R-001** (ubiquitous). WHEN the dashboard renders, THEN sections SHALL appear in
  mirror-tile order: practice mirror → dead-man → last seen (collapsed) → SDD panel →
  reuse + never-read themes → write→read loop → failed-search chart + top zero-hit →
  tokens trend → recent sessions.
- **R-002** WHERE every job is inside its cadence threshold, THEN the dead-man block
  SHALL render as a single strip line carrying the job total, the oldest job's silence
  age, and that job's own limit.
- **R-003** WHERE any job is red, THEN each red job SHALL additionally render its full
  tile (machine · agent · job, days silent) beside the strip line.
- **R-004** WHEN the dashboard renders, THEN the last-seen tiles SHALL sit collapsed
  inside a `<details>` element whose summary names the agent count and the newest
  event age.
- **R-005** WHERE service sessions exist in the window, THEN one disclosure line
  SHALL state their count, their agents, and that practice metrics below exclude
  them (ADR-0039).
- **R-006** WHERE no service sessions exist in the window, THEN the disclosure line
  SHALL be omitted.
- **R-007** (ubiquitous). The dashboard SHALL NOT render the week-over-week section,
  the reads-over-time-by-layer chart, the reads-by-content-type list, the
  reads-by-theme chart, the top-skills list, the sessions-by-repository table, the
  constitution-fleet panel, the standalone service-sessions section, or the kb-touch
  chart.
- **R-008** (ubiquitous). DashboardGold SHALL NOT carry ReadsByLayerDaily,
  KbTouchDaily, ThemeReads, SessionsByRepo, TopSkills, ReadsByContentType, or
  WeekOverWeek; the computations and record types die with the fields (ad-hoc
  analysis lives in silver).
- **R-009** (ubiquitous). The constitution-fleet summary SHALL stay on the report
  stdout line and in gold json (scan and `ConstitutionFleet` field unchanged).
- **R-010** (ubiquitous). The theme aggregation query SHALL keep feeding
  UnusedThemes (never-read themes); only its per-theme reads series leaves gold.
- **R-011** (ubiquitous). Everything not named above SHALL keep its current
  computation and rendering: mirror tiles (BL-033 semantics), SDD panel, reuse,
  write→read loop, failed-search chart, top zero-hit searches, tokens trend, recent
  sessions table, daily digest pages, dead/hot/stale worklists.

## Hurting case (the one it must never break)

**GIVEN** the live report of 2026-08-28 (failed-search corridor 20–30%, goal ≤15%),
**WHEN** the dashboard renders,
**THEN** the practice mirror still shows failed-search 🔴 «в коридоре 20–30% · до
цели −14пп» with no amber,
**AND** the failed-search chart and top zero-hit list still render unchanged in the
new order,
**AND** no week-over-week, sessions-by-repository, top-skills, reads-by-layer,
reads-by-content-type, kb-touch, fleet, or standalone service-sessions section
appears anywhere on the page,
**AND** gold json still carries `serviceSessions` (disclosure feeder),
`constitutionFleet`, `failedSearchDaily`, `tokensDaily`, and `unusedThemes`.

## Clarifications (grill session 2026-08-28)

1. Amber-threshold rebuild against 2 weeks of data → deferred to the ~Sep-10
   recalibration; the declutter stays a pure cut. (operator, recommended accepted)
2. Week-over-week section → dead forever as a section; its computation's stated
   consumer ("SearchTile feeds on it") does not exist in code — mirror tiles compute
   their own windows — so on the corrected fact the computation dies too, per the
   constitution rule of clarification 4. (operator, recommended accepted)
3. Dead-man → a single strip line when healthy; any red job restores its tile.
   (operator, recommended accepted)
4. Gold json cut series → cut per the constitution ("a field exists only if a
   question requires it"); ad-hoc analysis lives in silver; a future lens resurrects
   what a real question requires. (operator, recommended accepted)
5. Report line → no new Markdown report lines from cut content; registration
   candidates stay with `kbo audit completeness`; the fleet already has its stdout
   summary line + gold json. (operator, recommended accepted)

Mechanical/code-fact decisions at spec time (record, not operator-level, unless
noted):

- kb-touch chart + KbTouch daily computation: orphaned by both session lists, feeds
  no mirror tile (the six tiles are cache/burner/failed/loop/single-use/SDD) → cut
  under ADR-0042. (operator, recommended accepted)
- ServiceSessions computation survives the cut: it feeds the mandatory ADR-0039
  disclosure line (a note is not a section); only the h2 section dies.
- ThemeReads series field dies; the ReadsByTheme query stays (UnusedThemes depends
  on it).
- TouchedSessions stays: the recent-sessions KB column feeds on it.
- `charts/reads-over-time.vl.json`, `charts/reads-by-theme.vl.json`,
  `charts/kb-touch-rate.vl.json` deleted; the csproj wildcard needs no change.

## Converge (2026-08-28)

Audited against every R-line and the hurting case, code over diff:

- R-001: proven by `Render_SectionsFollowMirrorTileOrder` (index ordering of
  all eleven anchors) and the live render — h2 sequence matches the spec list.
- R-002/R-003: strip line + red-tile restoration covered by
  `Render_DeadManAllHealthy_SingleStripLine_NoTiles` /
  `Render_DeadManRedJob_RestoresItsTile`; live render shows
  «Dead-man: 9/9 ok · oldest archive 0.8d / limit 3d».
- R-004: `Render_LastSeen_CollapsedIntoDetails` + live
  «Last seen in bronze — 3 agent(s) · newest 0.7d ago».
- R-005/R-006: `Render_ServiceSessionNote_StatesTheExclusion` /
  `Render_NoServiceSessions_OmitsTheNote`; live line present (6 sessions,
  service-fleet).
- R-007: `Render_CutSections_AreAbsentEverywhere` (nine absence asserts) +
  `Render_FleetNeverRenders_EvenWhenConfigured`; live grep for all cut
  section names and chart ids: 0 occurrences.
- R-008: enforced by compilation (record fields removed with their types);
  live gold json keys carry none of the cut series.
- R-009: live stdout «fleet: 9 repo(s), 0 behind v23» + `constitutionFleet`
  in gold json; fleet scan tests untouched and green.
- R-010: `UnusedThemes_ListsUnreadThemesWithNoteCounts` /
  `UnusedThemes_ReadsOlderThanWindow_CountAsUnused`; the query survives in
  `ReadsByTheme` (single return value).
- R-011: 302/302 tests green; reuse, loop, recent-sessions, zero-hit, tokens,
  SDD, mirror, daily-digest, worklist tests untouched or retargeted and
  passing. Two KB-touch tests retargeted to the surviving consumer
  (recent-sessions TouchedKb); one deleted as redundant with
  `RecentSessions_NewestFirst_...`.
- Hurting case: proven live — failed-search 🔴 «в коридоре 20%–30% · до цели
  −14пп», chart and zero-hit list render in the new order, no cut section
  anywhere, gold json carries `serviceSessions`/`constitutionFleet`/
  `failedSearchDaily`/`tokensDaily`/`unusedThemes`.
- Out-of-scope respected: mirror calibration untouched (deferred ~Sep-10),
  daily digest keeps its own day facts, kbl side untouched beyond the
  sanctioned link line, report stdout line unchanged in shape.
- Gates: build 0 warnings / 0 errors, 302/302 tests, `sdd-lint` rc=0,
  `anchors` rc=0.

✅ Converged
