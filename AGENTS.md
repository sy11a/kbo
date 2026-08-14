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
  entry in CI; breaking changes get a read-time upcaster.
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
