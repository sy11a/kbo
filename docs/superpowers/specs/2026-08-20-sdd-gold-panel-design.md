# SDD practice gold panel — design spec

Date: 2026-08-20 · Feeds: legislator BL-026 (kbo gold panel gate, recipe in
`docs/superpowers/specs/2026-08-19-sdd-landscape-research.md` § "kbo
measurement recipe", amended by the 2026-08-20 consistency review:
generated writes must not read as documentation discipline).

## Why

The legislator's SDD law (edition v17, BL-032) will change how the fleet
works — specs in case files, ceremony tiers, analyze/converge gates.
Rolling it out blind means "it got better" is unfalsifiable. kbo already
captures everything needed (spec/plan reads/writes with session/repo/time
fidelity; `skill.invoked` events): the gaps are gold-side only, additive
per ADR-0002. This panel is the **before** snapshot — it must land and run
before v17 reaches the fleet.

## What — three metrics, one dashboard section, 60-day window

All three count **practice sessions only** (silver `practice_events`,
ADR-0039 service split; service rollouts of the law itself must not
pollute the before/after).

1. **Spec-before-code ordering.** Per session: *spec activity* = any
   `knowledge.read`/`knowledge.written` whose subject contains the path
   segment `/docs/superpowers/` **or** `/docs/cases/` (the current spec
   home and the legislator v17 case-file home — the panel must see
   across the transition it exists to measure; convention-in-code
   posture per ADR-0036); *code write* =
   `knowledge.written` whose `ContentKind.Of(subject)` is code. A session
   enters the denominator when it has ≥1 code write; it is *spec-first*
   when its earliest spec activity strictly precedes its first code
   write. Rows per repo × ISO week, plus a fleet summary rate.
2. **Writes by content kind.** `knowledge.written` grouped by
   `ContentKind.Of` — the docs-vs-code balance of what agents produce.
   **Machine-managed subjects (a `/docs/ai/` segment) are excluded**:
   fleet-law copies and (once v18 lands) the generated baseline are
   machine writes, not documentation discipline. Mirrors the existing
   reads-by-content-type shape (ADR-0025).
3. **SDD-skill rate.** Share of practice sessions with ≥1
   `skill.invoked` whose `data.skill` is in the configured set. The set
   is **registry config, never code** (no-hardcoding principle): an
   optional top-level `sdd:` block with `skills:` — the ADR-0031/0038
   opt-in pattern. No block → metrics 1–2 render normally and the skill
   table shows a "not configured" note (absence is stated, never silent).

## Shape

```csharp
record SddOrderingRow(string Week, string Repo, long CodeSessions, long SpecFirstSessions, double Rate);
record SddOrderingSummary(long CodeSessions, long SpecFirstSessions, double Rate);
record SddWritesRow(string Kind, long Writes);
record SddSkillRateRow(string Repo, long Sessions, long SddSessions, double Rate);
record SddPanelGold(Ordering rows, OrderingSummary, WritesByKind rows, SkillRate rows, bool SkillConfigured);
```

- `DashboardComputer` gains `SddPanel(connection, registry, now)` — same
  home as every other silver-derived gold fact; session→repo via the
  `sessions` view (`(unknown)` when absent, `SessionsByRepo` precedent).
- Ordering bucket = ISO week of the session's first code write
  (`2026-W34`), rows capped at 50 (`RepoListCap` precedent), newest week
  first.
- Renderer: one `<h2>` section after the service-sessions note, Russian
  descriptions, plain tables — no new charts (the trend question is
  before/after across reports, not intra-report).

## Non-goals

No session-outcome metric (honest limitation, recipe § "Honest
limitation"); no commit-level tracking (`knowledge.written` on spec paths
already sees pre-commit writes — arguably better); no per-user split
(solo fleet); no new events or schema changes.

## Testing

Fixture tests mirroring `DashboardComputerTests`: ordering classification
(spec-first / code-first / code-only / spec-only), machine-managed
exclusion in writes-by-kind, configured vs unconfigured skill rate,
registry `sdd:` parse (valid + empty-list rejection), renderer section +
unconfigured note. Full suite green via `dotnet test`.

## Done when

Panel renders from live silver with all three metrics; tests green; OKF
dashboard.md + log.md updated; ADR-0040 records the design decisions.
