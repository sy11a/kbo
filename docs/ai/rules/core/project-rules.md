## Project Rules

- Project-specific rules — law that applies to this repo only ("every feature ships behind a feature toggle") — live in `.claude/rules/`, one topic per file (`feature-toggles.md`, `e2e-tests.md`). Claude Code loads every `.md` there automatically at session start; no `@import` wiring is needed.
- Write them law-shaped: short, imperative, checkable against a diff. How-to guidance is not law — link to where it lives (docs, skills) instead of inlining it.
- When a rule only applies to part of the tree, scope it with `paths:` YAML frontmatter (glob patterns, e.g. `src/api/**/*.ts`) so it loads only when matching files are touched.
- Never put project rules in `docs/ai/rules/**` — that is machine-managed fleet law, overwritten byte-for-byte on every legislator run. Never inline them into CLAUDE.md's body either — CLAUDE.md stays lean; `.claude/rules/` is the project-law home.
- Keep each rule file small (aim under ~30 lines): unscoped rule files enter context every session.
- Two sanctioned instance-data homes: `.claude/rules/verification.md` (verification bindings) and `.claude/rules/skills.md` (skill sanction/stage lists) hold concrete tool bindings by design — restructuring must not propose relocating their instance data to CLAUDE.md.
- If a project rule would make sense verbatim in another repo of the same stack, it is a constitution candidate — legislator runs will propose promoting it to fleet law.
