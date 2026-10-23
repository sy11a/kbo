# kb-observability — Project Instructions

## Project Overview

`kbo` is a C# CLI for **Practice Observability**: it captures AI-agent
knowledge-usage events into an append-only NDJSON *bronze* store, derives a
disposable DuckDB *silver* layer, computes *gold* facts exactly once, and
renders a static Vega-Lite dashboard plus Markdown worklists into a knowledge
vault. Architecture decisions are recorded as ADRs in `docs/adr/`.

Stack: `.NET`

- OKF bundle: `docs/okf/` — living documentation of every concept; keep it in
  sync with the code.
- Domain glossary: `docs/okf/glossary.md` — check it when a term is unclear;
  add terms as they emerge.
- Project rules: `.claude/rules/` — one law file per topic (auto-loaded by
  Claude Code; opencode loads them via `opencode.json`'s `instructions`).

@docs/ai/rules/core/okf.md
@docs/ai/rules/core/pair-development.md
@docs/ai/rules/core/decision-gate.md
@docs/ai/rules/core/adr.md
@docs/ai/rules/core/dev-journal.md
@docs/ai/rules/core/changelog.md
@docs/ai/rules/core/artifact-lifecycle.md
@docs/ai/rules/core/project-rules.md
@docs/ai/rules/core/skills.md
@docs/ai/rules/core/verification.md
@docs/ai/rules/stacks/dotnet/architecture.md
@docs/ai/rules/stacks/dotnet/coding-standards.md
@docs/ai/rules/stacks/dotnet/data-access.md
@docs/okf/codebase-map.md

## Architecture principles

These govern every change; a reviewer should be able to check a diff against them:

- **Bronze is immutable and sufficient** — `kbo rebuild` must reproduce silver
  from bronze alone. Never mutate past bronze lines.
- **Gold is computed exactly once** — renderers contain zero computation.
- **Schema evolution is additive-only** — a new version plus a golden-corpus
  entry in the test suite; breaking changes get a read-time upcaster.
- **Nothing agent-specific or path-specific is hardcoded** outside the adapters
  and the typed registry.
- **The NOT-list (ADR-0003) is a hard boundary** — no custom storage engine,
  web server, resident daemon, OTel collector, or real-time anything without a
  decision recorded as an ADR.

## Boundaries

Generated build output only (`bin/`, `obj/`, `node_modules/`) — do not edit
generated files.

## Build & Test

- Build: `dotnet build`
- Test: `dotnet test`
- Architecture: `dotnet tool run sy11a-arch check --affected kb-observability.slnx`
- Test gate: `dotnet tool run sy11a-test check --affected --base origin/master kb-observability.slnx`

### Gate evidence for review

The tool side of the dev-flow review evidence (dev-flow BL-522, ADR-0033), bound for this repository
(sy11a/sy11a_analyzers#327). None of the three blocks a task by itself; review judges them.

- **Deleted tests.** The test-gate row's report (`sy11a-test check --affected`) lists the test methods the
  diff deleted or that lost `[Fact]`/`[Theory]`. That list goes into the review's gate tail. A deletion is
  not an error.
- **Mutation round.** After a green test gate, run
  `dotnet tool run sy11a-test mutate --affected --base origin/master kb-observability.slnx`.
  - The surviving mutants (file, line, original and replacement, mutator, covering tests) go to the
    Claude-family test writer for one round: tests only, no production code.
  - Then the test gate and `mutate --affected` run again over the same lines. What still survives is the
    test-plan note's survivor record.
  - Exit `2` means the round could not run. The note says so in one line and the task goes on.
  - Exit `143` (a signal stopped it; sy11a.test 0.18.0 and later) is the same, except that the line says
    "interrupted", and the survivors of the pairs that finished, in the JSON, still go into the record.
- **Duplicate-detector evidence.** The duplicate gate is `sy11a-arch` SYA1201 (jscpd), part of the arch row.
  Review records the clones it missed and the SYA1201 hits it judges false under "Duplicate-detector
  evidence" in the review report.
