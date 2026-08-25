# Cases

Every unit of work in this repo is a **case** (`BL-NNN`), and every case
has exactly one home: `docs/cases/BL-NNN/` — its spec, plan package, and
summary live together, and the backlog's case-register row links here.

The law governing cases is `docs/ai/rules/core/sdd.md`. The essentials:

- **Tier line** — every case declares its ceremony tier in its header
  (in the summary or spec): `Tier: 0 (direct)` / `Tier: 1 (light)` /
  `Tier: 2 (full)`. Chosen at case opening on blast radius × novelty.
  Tier 0 is lawful, not a shortcut; converge audits every tier.
- **Spec type** — feature / bugfix / exploration, declared in the spec
  header. Bugfix specs state current / expected / **unchanged**.
- **Requirements** are EARS lines with stable ids (`R-001`…) and at
  least one named hurting-case scenario; clarification answers land in
  the spec's `## Clarifications`.
- **Plans are packages** — research / data-model / contracts / quickstart
  as the domain demands; tasks trace `per R-NNN`; `[P]` marks
  file-disjoint parallelizable tasks.
- **Gates** — analyze before implementation; converge before closing
  (no exceptions, every tier). A case closes only on "✅ Converged".

Historical specs and plans live under `docs/superpowers/` — that path is
legacy history: never rewritten, never moved; their register rows link
to them where they lie.

Naming: `BL-NNN-short-description/` matching the branch `bl/NNN-…`
convention.
