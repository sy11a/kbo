## OKF Documentation Rule (MANDATORY)

The `docs/okf/` directory is an Open Knowledge Format bundle. It is the living documentation of every concept in this system. **You MUST update the corresponding OKF document whenever you implement, refactor, or change a concept.**

### What maps to what

See this project's `docs/okf/index.md` for the category-to-file mapping table specific to this codebase.

### OKF update checklist (run on every task completion)

- [ ] All new concepts have an OKF document created
- [ ] Changed concepts have their OKF document updated (properties, decisions, file paths)
- [ ] `status` field updated: `planned` → `implemented` (or `partial`); removed concepts flip to `removed`
- [ ] `timestamp` field updated to today's date (ISO 8601)
- [ ] New cross-links added where relevant (`[text](../path/to/doc.md)`)
- [ ] New or renamed domain terms have a row in `docs/okf/glossary.md`; meanings of changed terms updated
- [ ] `docs/okf/log.md` has a new entry describing what changed and why

### When to update

- **During implementation** — update OKF as you write the code, not after
- **During refactoring** — if you rename a class, move a file, or change a method signature, update the OKF doc that references it
- **On every task completion** — run the checklist above before committing

## OKF is non-negotiable

- **Always update the relevant `docs/okf/` document** — before writing code, during implementation, and on completion. No exceptions.
- The OKF checklist above must be run on every task completion before the final commit.
