# BL-033 — Mirror calibration v1 (emoji state model)

**Spec type: feature**
**Tier: 1 (light — EARS spec + hurting case; operator decisions pre-fixed in the
2026-08-27 design session, backlog commit `110b1fc`)**

- **Branch:** `bl/033-mirror-calibration-v1`
- **Open:** 2026-08-28

## Intent

Replace the mirror's a-priori ok/amber/red thresholds with a calibrated state model:
**state = emoji, not color**; amber is reserved for «ТРЕБУЕТ ВНИМАНИЯ СЕЙЧАС» (acute).
A chronically-bad tile stops being a permanent amber (alarm fatigue) and becomes a 🔴
with a goal gap — a backlog item, not a color.

## Boundary

**In scope:** the six existing mirror tiles switch to calibrated states; weekly-snapshot
history + corridors (p25–p75) computed in gold; additive `MirrorTile` fields; renderer
legend/styling; tests at math, computer (integration), and renderer levels.

**Out of scope:** dashboard declutter (separate case); ADR-0042 text; `notify-send`
on acute breaks (v1 is visual-only); future tiles (link-density, entry-overhead — they
will render ⏳ placeholders when they arrive); per-model cache normalization; goal
ratchet; anything outside `DashboardComputer`/`DashboardGold`/`DashboardRenderer` and
their tests.

## Requirements

- **R-001** (ubiquitous). WHEN a report runs, THEN each mirror tile SHALL be evaluated
  over its declared window — 14 days for cache/burner/failed-search, 42 days for
  loop/single-use/SDD — at a weekly Monday-UTC snapshot grid over the trailing 8
  completed weeks, plus the current live value at `now`.
- **R-002** WHERE at least 6 completed-week history points exist, THEN gold SHALL carry
  per tile the corridor (p25/p75, linear-interpolated percentiles), median, and MAD of
  the history points.
- **R-010** WHEN a report renders the mirror, THEN the renderer SHALL read every
  calibration field from gold — zero threshold constants in renderer code.
- **R-003** WHERE fewer than 6 history points exist, THEN the tile SHALL render the
  placeholder «⏳ собираю историю: N/6 нед» and no state.
- **R-004** WHEN at least 6 points exist, THEN the tile SHALL be ⚠️ (acute, the only
  amber) iff the robust z-score `|value − median| / (1.4826 × MAD)` exceeds 2; WHERE
  MAD < 1e-9 (saturated series), THEN iff `|value − median| > 0.02` (2pp absolute floor).
- **R-005** WHEN no acute break occurred and the tile is goal-bearing, THEN it SHALL
  show 📈/📉 iff the OLS slope of the history points (per week) exceeds in absolute
  value 2 × MAD of the weekly first differences.
- **R-006** WHEN neither acute nor trend, THEN the tile SHALL be 🟢 iff the whole
  corridor sits inside the goal, else 🔴 (including a corridor that straddles the goal).
- **R-007** Goal-bearing tiles SHALL carry a static goal line («цель ≤15% · до цели
  −13пп»), self-computed each report from the same value (no renderer arithmetic).
- **R-008** Cache-discipline and burner tiles SHALL be trust tiles: no goal, ⚠️ on acute
  break only, otherwise 🟢.
- **R-009** ⚠️ SHALL be the only amber-styled tile.
- **R-011** 🔴 SHALL NOT use alarm styling — chronic sickness routes to the backlog,
  not to color.

## Hurting case (the one it must never break)

**GIVEN** 8 weeks of history with failed-search weekly values
[8.5, 36.6, 31.8, 21.7, 19.8, 23.7, 27.0]% and a current value of 28% (goal ≤15%)
— today's live shape:
**WHEN** a report renders the mirror,
**THEN** the failed-search tile shows 🔴 with «до цели −13пп», no amber, no ⚠️
(z ≈ 0.7 < 2);
**AND WHEN** a single acute week pushes the live value to 45% (z > 2),
**THEN** and only then does the tile turn ⚠️ amber.

## Clarifications (session 2026-08-28)

1. **Corridor straddles the goal** (p25 inside, p75 outside)? → **🔴** «неустойчиво у
   цели»; 🟢 requires the whole corridor inside the goal. (operator, recommended accepted)
2. **«Цель самозатягивается при достижении»** → **v1: static goal** + gap line; the
   ratchet (goal re-anchors to the best historical corridor after first achievement)
   is a backlog line, not code. (operator, recommended accepted)
3. **Cross-model cache shift** (Claude ≈ 1.00 vs GLM ≈ 0.93–0.96, verified in silver)
   → **v1: one global corridor**; the tile is trust-mode (acute only), and model-mix
   shifts are gradual, not acute. Per-model normalization is a future lens.
   (operator, recommended accepted)

Mechanical decisions taken at spec time (record, not operator-level):

- Snapshot grid: Mondays 00:00 UTC; history = **completed** weeks only (the current
  week contributes only the live value). Snapshots with an empty denominator are
  skipped; history horizon = trailing 8 completed weeks (bounded so old practice
  ages out).
- Percentiles: linear interpolation between order statistics (numpy default).
- Trend threshold: OLS slope over history points vs `2 × MAD` of the weekly first
  differences (MAD of the values themselves would make the threshold unreachable for a
  perfectly linear ramp — value-MAD ≈ 2 × slope); zero-difference MAD (perfectly linear
  series) flags, a flat series (slope 0) never does. Precedence: acute > trend > goal
  state.
- z-scale: `1.4826 × MAD` (robust σ estimate); saturated floor 2pp absolute.
- Trust tiles suppress trend (⚠️ or 🟢 only) — a slow mix-shift must not nag.
- Windows change vs v0.1: tokens/search 7d → 14d; loop/single-use/SDD 60d → 42d
  (mirror only — the detailed sections keep their 60-day windows).
- Goals (instrument constants, live in `DashboardComputer`, emitted into gold):
  failed-search ≤ 0.15, loop ≥ 0.30, single-use ≤ 0.55, spec-before-code ≥ 0.50;
  cache/burner trust (no goal).

Data checks on live silver (2026-08-28, read-only):

- History starts 2026-07-07; weekly snapshots are computable immediately for all six
  tiles (7–8 non-empty points each) — no backfill needed.
- Corridor stability: p25/p75 over weeks 1–6 vs 2–8 drift < 5pp per metric.
- `hits` is null in 3.6% of `knowledge.searched` (best-effort field; excluded from
  the denominator, unchanged).
- Burner threshold 100k input tokens sits beyond p95 (80k) — 3 of 3393 sessions;
  acceptable for a trust tile.
- Live failed-search corridor ≈ [20%, 32%] vs goal 15% → 🔴 «до цели −13пп»,
  exactly the design's motivating case.

## Converge (2026-08-28)

Audited against every R-line and the hurting case, code over diff:

- R-001..R-011: satisfied. R-002 initially shipped without median/MAD in
  gold (found by this audit) — `MirrorTile` now carries both, asserted in
  the integration test and verified in the live gold json.
- Hurting case: proven twice — integration test (seeded weeks) and the live
  report (failed-search 🔴 «в коридоре 20–30% · до цели −14пп», no amber).
- Two implementation-time corrections recorded in Clarifications: trend
  threshold re-based on weekly first differences (+0.5pp/week absolute
  floor); grid-ordering bug (antichronological snapshots inverting slope
  sign) caught by the integration test before it ever reached silver.
- Out-of-scope respected: declutter, ADR-0042, notify-send, per-model
  normalization, ratchet untouched (Deferred rows in the backlog).
- Open for the ~Sep-10 recalibration (journal 2026-08-28): first live
  render flags three ⚠️ acutes on the 42d-window tiles (z = 13 / 3 / 9.6) —
  overlapping windows give tight corridors, and a real writing burst plus
  the legislator rollout register as sharp breaks. Behavior per spec; the
  diff-scale for slow tiles is the recalibration's question.
- Gates: build 0 warnings, 305/305 tests, `sdd-lint` clean, `anchors` clean.

✅ Converged
