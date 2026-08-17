---
type: Component
title: Registry (Corpus) — the typed map of knowledge on a machine
description: Per-machine YAML registry of knowledge sources (id/layer/root), kbroot path resolution, and the `kbo registry` CLI.
tags: [component, registry, corpus, kbroot]
timestamp: 2026-08-16T00:00:00Z
status: implemented
---

# Registry (Corpus)

The typed, hand-maintained map of knowledge on a machine: which directories are knowledge sources, and what layer each belongs to. Spec: `~/Agent/Practice Observability/07 - Feature Specs.md` §Registry; decisions G2-1, G2-9, G2-10; implementation decisions [ADR-0005](../adr/0005-typed-registry-implementation.md).

## Shape

```yaml
machine: example-machine
taskPattern: 'AC-\d+'   # optional — branch → task id regex (ADR-0031)
constitution:            # optional — legislator fleet panel (ADR-0038)
  versionFile: /home/admin/Repository/legislator/skill/VERSION
  scanRoots:
    - /home/admin/Repository
sources:
  - id: knowledge
    layer: global      # global | framework | local | skills
    root: /home/admin/Knowledge
  - id: repo
    layer: local
    root: /home/admin/Repository/*/docs   # glob root (ADR-0019)
```

- The **real** registry is a machine-local overlay at `~/.config/kbo/registry.yaml`; precedence `--registry` flag → `KBO_REGISTRY` env var → XDG default. Never committed (G2-9).
- The repo commits a **sanitized example** at `registry/example.yaml`.

## Behavior

- **kbroot tagging (G2-1)**: an event's subject path resolves to the `id` of the source whose `root` contains it; paths under no registered root get `kbroot: null`. Nested roots resolve to the longest matching root; prefix matching is segment-safe and does not resolve symlinks.
- **Glob roots (ADR-0019)**: a root may contain `*` as a whole path segment (`/home/u/Repository/*/docs`); it expands at load time to one concrete source per matching directory, id `<entry-id>-<matched-dir>` (e.g. `repo-kb-observability`). Future directories are picked up automatically on the next load; zero matches is valid, partial-segment stars (`Repo*`) are rejected.
- **Glob excludes (ADR-0034)**: a glob source may carry `exclude: [dirname, ...]`; a candidate whose `*`-matched directory name is listed is skipped during expansion (e.g. an archived repo that must not enter the inventory). `exclude` on a non-glob source is a validation error.
- **Inventory excludePaths (ADR-0036)**: any source may carry `excludePaths: [subdir, ...]` — relative, glob-free subtrees under the root that the note inventory skips (tool fixtures, benchmark data). Glob sources propagate the list to every expanded source. Inventory-only: `Resolve`/kbroot tagging are unaffected.
- **Constitution fleet (ADR-0038)**: optional top-level `constitution:` block — `versionFile` (absolute path to the legislator `skill/VERSION`) and `scanRoots` (absolute dirs whose **direct children** are candidate legislated repos, detected by `docs/ai/manifest.json`). Optional `exclude: [dirname, ...]` — plain directory basenames the scan skips (e.g. an archived repo that keeps its manifest but is never upgraded). Powers the dashboard "Constitution fleet" panel; no block → no panel. A configured-but-missing `versionFile` fails the report loudly.
- **Task pattern (ADR-0031)**: optional top-level `taskPattern` — a .NET regex whose first match in a git branch name becomes the event's `task`; `KBO_TASK_PATTERN` env var overrides it. Unset means no task extraction: `task` is always `null`. No default pattern ships — a ticket convention is org-specific configuration, not tool behavior.
- **Strict validation**: unknown layer, duplicate id (including glob-expanded ids), relative root, missing fields, or an invalid `taskPattern` regex throw `RegistryFormatException` naming every problem — the registry is load-bearing; it must fail loudly, not rot silently.
- The registry doubles as the **denominator**: the note inventory (all files under all roots) is what "dead" is measured against.

## Implementation

- `src/Kbo/Registry/` — `KnowledgeRegistry` (`Parse`/`Load`/`Resolve`), `KnowledgeSource`, `KnowledgeLayer`, `ConstitutionConfig`, `RegistryLocator`, `RegistryFormatException`
- `src/Kbo/Cli/RegistryCommand.cs` — `kbo registry show`, `kbo registry resolve <path>`
- `src/Kbo/Gold/ConstitutionFleet.cs` — the fleet scan the `constitution:` block powers
- Tests: `RegistryParseTests`, `RegistryResolveTests`, `RegistryValidationTests`, `RegistryLocationTests`, `RegistryCommandTests`, `ConstitutionConfigParseTests`, `ConstitutionFleetTests`

## Links

- [Schema registry](schema-registry.md) — events carry the `kbroot` field this registry resolves
- [Glossary](glossary.md) — `registry`, `kbroot`, `layer`
