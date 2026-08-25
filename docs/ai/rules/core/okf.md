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

### Link hardness — what each document is bonded to

Hand-maintained truth rots; the parts of this repo that cannot rot are the
parts a machine writes or checks. The bundle is three classes, and a
document's class decides what can be checked about it:

- **anchored** — `index.md`, `codebase-map.md`, and every concept document.
  Hand-written, and every path or symbol it backticks resolves in this
  repository. A broken anchor is a document describing code that is gone.
- **human** — `glossary.md` and `log.md`. Anchoring does not apply: a glossary
  defines terms and a log records what was true at the time, so naming
  something since removed is correct there, not stale. A document whose front
  matter carries `status: removed` joins them for the same reason: the
  checklist above tells you to keep it and mark it, so the code it names is
  *meant* to be gone. Anchoring it would make obeying the checklist block
  every later task in the repository, since the rung in `verification.md` is
  repo-global and restructure routes such findings to the team by design.
- **generated** — written by a machine from a source it mirrors, never
  hand-edited, regenerated on demand. Declared here; this bundle has no
  generated member yet.

**What counts as an anchor (closed).** A backticked token in an anchored
document that carries no space, none of `<`, `>`, `*`, `?`, and does not start
with `~` or `/`. It is a **path-anchor** when it contains `/` and its first
segment is a top-level directory of this repository — it resolves when that
path exists (a trailing `.Member()` is stripped first). It is a
**symbol-anchor** when it is PascalCase of at least four characters,
optionally dotted (`Type.Member`) — it resolves when its leading segment
occurs literally under this repository's source roots (every non-hidden
top-level directory except `docs/`, `bin/`, `obj/`, `node_modules/`,
`dist/`). Everything else a document backticks — commands, field names,
lowercase identifiers, templates — is not an anchor. A symbol-anchor asks
whether the identifier still exists, not whether its declaration kept its
shape, and a dotted anchor asks only about the type it names — the member
half is prose.

- **`python3 docs/ai/engine.py anchors` is the executing arm of this rule** —
  it writes nothing and reports every anchor that no longer resolves.
  `core/verification.md` carries the rung that requires it before "done".
- **`python3 docs/ai/engine.py okf-debt`** names anchored documents whose
  sources moved on without them: an anchored source file with a commit more
  than 30 days newer than the document's own newest commit. A path-anchor
  naming a directory is not a source for this purpose — a directory's history
  is the union of everything beneath it and can never say whether one document
  went stale. Repair is an ordinary OKF update by the document's owner —
  never an automatic rewrite.

## OKF is non-negotiable

- **Always update the relevant `docs/okf/` document** — before writing code, during implementation, and on completion. No exceptions.
- The OKF checklist above must be run on every task completion before the final commit.
