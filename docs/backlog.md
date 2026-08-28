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

## Mirror calibration v1 — emoji-модель состояний (design session 2026-08-27, операторские решения зафиксированы)

Контекст: пороги зеркала априорные; 8 недель истории показали: cache-discipline/burner насыщены
(0.96–1.0 / ~0%), failed-search стабильно 25–30% при цели ≤15% — вечно-янтарная плитка
приучает не смотреть. Решение: состояние = эмодзи, не цвет; янтарь зарезервирован за
«ТРЕБУЕТ ВНИМАНИЯ СЕЙЧАС».

**Модель состояний (DashboardComputer + gold json):**
- личный коридор p25–p75 своей истории; окна: токены/поиск — 14д, loop/single-use/SDD — 6 недель
  (история уже в silver с 06.07 — коридоры считаются сразу, бэкфилла не нужно)
- 🟢 стабильно-здорово: внутри коридора И коридор внутри цели
- 🔴 стабильно-больное: внутри коридора И коридор вне цели (хроника → бэклог, не цвет)
- 📈/📉 тренд: наклон по неделям > 2×MAD (робастный порог: медиана абсолютных отклонений
  собственной истории; одна аномальная неделя не раздувает коридор, как σ)
- ⚠️ ТРЕБУЕТ ВНИМАНИЯ СЕЙЧАС (единственный янтарь): острый выход за коридор, z > 2
- цель — строкой на плитке («цель ≤15% · до цели −13пп»), самозатягивается при достижении
- насыщенные плитки (cache/burner) — trust-tiles: без цели, только ⚠️ при остром сломе
- будущие плитки без истории (link-density, entry-overhead) — плейсхолдер «⏳ собираю историю: N/6 нед»
- ⚠️-уведомление: v1 визуально-only (notify-send добавить при первом пропущенном остром случае)

**Решено оператором:** коридоры живут в gold json (пересчёт каждым report), рендер только
читает — констант в коде нет.

**Проверки при реализации:** (1) стабильность коридора: p25/p75 по неделям 1–6 vs 2–8 —
дрейф <5пп; (2) кросс-модельный кэш-сдвиг (Claude щедро / часть моделей не считают
cache_read) — возможно per-model с медианой; (3) hits=null доля в knowledge.searched
(best-effort поле); (4) порог burner 100k валидировать по распределению своих input.

**Сделано =** эмодзи-состояния в рендере, коридоры из истории в gold json, ⚠️ только острый
слом, ⏳-плейсхолдеры, тесты на каждое состояние, cache/burner переведены в trust-режим.
