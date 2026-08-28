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

## Register

| Case | What | Home |
|------|------|------|
| BL-033 | Mirror calibration v1 — emoji state model, p25–p75 corridors in gold json, trust-tiles, ⏳ placeholders (design session 2026-08-27; decisions + data checks inside) | [docs/cases/BL-033-mirror-calibration-v1/spec.md](cases/BL-033-mirror-calibration-v1/spec.md) |

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
