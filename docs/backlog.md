# kb-observability — Backlog

Tasks pending implementation. **Rule: update the relevant `docs/okf/` document first, before writing any code.** When a task is fully done, remove it from here.

---

## Dashboard declutter — answer only the mirror questions (2026-08-27 design session)

Rule: every section must feed a mirror tile or system trust. Session grill first (questions below).

**Cut from RENDER (8):** Week over week (дублирует тренды зеркала; вычисление ОСТАВИТЬ —
им питается SearchTile) · Reads-over-time by layer · Reads by content type · Reads by theme
chart (запрос оставить — из него UnusedThemes) · Top skills · Sessions by repo ·
Constitution fleet (сводка остаётся строкой report'а + gold json) · Service sessions.

**Cut from COMPUTER + DashboardGold + tests (мёртвые вычисления, конституция «поле
существует только если требует вопрос»):** ReadsByLayer · ReadsByContentType · TopSkills ·
SessionsByRepo · ServiceSessions.

**Keep & reorder to mirror-tile order:** Practice mirror → Dead-man (компактно) →
Last seen в <details> свёрнуто → SDD panel → Reuse + Unused themes → Write→read loop →
Failed-search chart + Top zero-hit → Tokens-trend → Recent sessions.

**Grill перед правкой:** (1) пороги amber-плиток против 2 недель данных — пересобрать
диапазоны? (2) WeekOverWeek-секция — вернуть чьим-то drill-down или мертва навсегда?
(3) Dead-man — плитки-грид или одна строка-стрип? (4) gold json: продолжаем считать
вырезанные серии для ad-hoc или режем по конституции? (5) Report-строка: добавить туда
вырезанное (fleet уже есть)?

## ADR-0042 — practice-first dashboard + место kbo в каноне

Канон: **legislator пишет законы · kbl ведёт фонд знаний · kbo ревизует практику.**
kbo = измерительный прибор: (a) зеркало шести вопросов, (b) dead-man системы (джобы +
канарейка), (c) владелец реестра источников — kbl регистрируется в нём как source, читает
silver/gold строго read-only. kbo никогда не хранит знание и не пишет законы; измерительная
поверхность одна (у kbl дашборда нет — его проекции в Obsidian).

**Grill:** (1) точная формулировка владения реестром; (2) fleet-панель — только строка
report'а или отдать legislator-репо свою сводку? (3) включить ли канарейку в ADR как
часть dead-man-обязанности kbo?

+ одна строка-ссылка на ADR-0042 в kbl/docs/okf/architecture.md (та же сессия).

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

## sdd-lint: bring the old ADRs to closed-set status shape

Legislator v23 introduced the ADR status shape-lint (closed set: proposed / accepted /
deprecated / superseded by NNNN), but 25 ADRs predate it — `python3 docs/ai/engine.py sdd-lint`
fails: 18 (0001–0015, 0020, 0022, 0031) carry the annotation inside the status line itself
("accepted (owner decision …)"), and 7 more (0034–0040) have no `## Status` section at all —
a one-line "Status: accepted · Date: …". Mechanical fix, meaning preserved verbatim: the
status is a single token from the set, annotation/date move to a line below; for 0034–0040,
expand into the section. Done when `sdd-lint` exits 0; only the header shape changes, the
content of the decisions is untouched.

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
