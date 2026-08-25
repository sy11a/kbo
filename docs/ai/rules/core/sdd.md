## SDD Practice Rule (MANDATORY)

Every unit of work is a **case** (`BL-NNN`), and every case runs under this law. Specs written outside a case file, plans without traceability, and code merged past an unconverged case are drift.

### Cases and their home

- **One case, one home** — everything a case produces (spec, plan package, summary) lives in `docs/cases/BL-NNN/`; the backlog/register row links into it. `docs/superpowers/**` is legacy: history stays where it lies, never rewritten, never moved.
- **Declare the tier at case opening** — tier 0 (direct: no spec; backlog line → branch → journal; converge still audits), tier 1 (light: EARS spec + hurting case + clarify), or tier 2 (full: research → contracts → analyze → implement → converge) — chosen on blast radius × novelty, recorded in the case header. Tier 0 is lawful, not a shortcut; inflating a tier is waste. Converge may raise a tier when it finds missing behavior.
- **Cross-repo cases live once** — the case file lives in the initiating repo; sibling repos carry reference rows and the same `bl/NNN` branches; converge judges the whole tree.

### Spec form

- **Declare the type in the header** — feature, bugfix, or exploration. A bugfix spec states current / expected / **unchanged** behavior; the unchanged list is the regression contract.
- **State the boundary** — in-scope and out-of-scope; the out-of-scope half is half the assignment. One intent per spec: "and also" is the split signal.
- **Requirements are EARS lines with stable ids** — one line, one behavior, one `R-NNN`, one SHALL (WHEN…THEN…SHALL / WHILE / WHERE / IF-THEN / ubiquitous). Ids are permanent: tasks, tests, and baselines reference them.
- **Ship the hurting case** — every spec carries at least one named GIVEN/WHEN/THEN scenario for the case it would hurt most to see broken. Every THEN and every SHALL response is observable: a tester who never read the code can tell whether it holds.

### Clarify before approval

- **Grill before a spec is approved** — max 5 questions, one at a time, recommended option first. Write every accepted answer into the spec's `## Clarifications` (dated session, Q→A); replace contradicted text, never duplicate it.

### Plans

- **A plan is a package in the case file** — research (Decision/Rationale/Alternatives), data-model, contracts, quickstart as the domain demands; split past ~10–15 KB or 2+ domains. One task = one session. Mark file-disjoint tasks `[P]`. Every task traces `per R-NNN`.
- **The ADR boundary** — a decision that outlives the case goes to ADR; a local one goes to research.md; plans carry neither.

### Gates

- **Analyze before implementation** — judge reuse-first (existing code checked before new code is planned) and over-engineering (nothing unrequested); run the mechanical passes (coverage R↔task, dangling R-NNN, unresolved placeholders) with the engine when available, otherwise as an explicit checklist. Findings are proposals; the human decides.
- **Converge before closing — no exceptions, every tier** — judge code against every promise (spec lines, plan decisions, constitutional MUSTs), never against git diffs. Classify gaps missing / partial / contradicts / unrequested. Append each finding as a traceable task (`per <source-ref> (<gap-type>)`) to the case plan — append-only, never rewriting. Constitutional violations are CRITICAL. Check OKF docs anchored to touched files: stale prose is a finding. Loop implement → converge until clean; a case closes only on "✅ Converged".
