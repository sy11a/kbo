# 0040 — SDD practice panel: convention-detected metrics, config-declared skill set, machine-managed writes excluded

Status: accepted · Date: 2026-08-20

## Context

The legislator's SDD law (edition v17) will change fleet practice — specs
in case files, ceremony tiers, converge/analyze gates. Rolling it out
without a baseline makes the improvement claim unfalsifiable. The
measurement recipe (legislator research spec, 2026-08-19) verified kbo
already captures spec/plan reads/writes and `skill.invoked` events with
session/repo/time fidelity; the gaps are gold-side only. Three design
questions needed answers: how to detect "spec activity" without
hardcoding user paths, whose writes count as documentation discipline,
and where the SDD skill-name list lives.

## Decision

1. **Spec activity by fleet path convention, in code.** A subject with a
   `/docs/superpowers/` or `/docs/cases/` segment is spec activity (read
   or write) — the current spec home and the v17 case-file home, both
   detected from day one so the before/after instrument stays valid
   across the transition it measures; a code write is
   `knowledge.written` with `ContentKind` code. The conventions are
   fleet-wide — every legislated repo uses these homes — so they belong
   in code beside the `/docs/ai/` and `/adr/template.md` conventions
   (ADR-0036 posture), not in registry config. Ordering is strictly
   per-session: earliest spec activity before first code write, sessions
   with no code write leave the denominator (they made nothing
   measurable).
2. **Machine-managed writes are excluded from the docs-vs-code metric.**
   Writes under `/docs/ai/` are constitution copies the tooling writes
   into every repo — and once legislator v18 lands, the generated
   `baseline.md` lives there too. Counting them would let a rollout
   machine-write its own "documentation discipline" up. Handwritten docs
   discipline is the metric; machine writes are not it.
3. **SDD skill set is registry config (opt-in, top-level `sdd:` block
   with `skills:`)** — the ADR-0031/0038 pattern: a public tool ships no
   default skill list, an absent block disables metric 3 with a stated
   "not configured" note (no silent omission), and the owner's skill
   names are owner data. An empty list is a config error.
4. **All three metrics count practice sessions only** (`practice_events`,
   ADR-0039): fleet rollout of the law itself runs as `service-*` and
   must not pollute the before/after. Window 60 days (`ThemeWindowDays`),
   ordering bucketed per repo × ISO week of the first code write; no new
   charts — the question is before/after across reports, so tables
   suffice.

## Consequences

- The panel is pure gold: no new events, no schema change, no adapter
  touch — additive per ADR-0002; `DashboardComputer`/`DashboardGold`/
  `DashboardRenderer` alone grow.
- The `/docs/superpowers/` + `/docs/cases/` conventions bake in the two
  known spec homes; a future third home would extend the detector (a
  one-line convention change, still fleet-wide).
- Skill-rate honesty depends on harvest lag (opencode transcript
  harvest): the panel measures captured sessions, and the recipe's noted
  limitation stands — adoption is measurable, "specs made code better"
  is not.
