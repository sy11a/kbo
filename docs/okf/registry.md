---
type: Component
title: Registry (Corpus) — the typed map of knowledge on a machine
description: Per-machine YAML registry of knowledge sources (id/layer/root), kbroot path resolution, and the `kbo registry` CLI.
tags: [component, registry, corpus, kbroot]
timestamp: 2026-08-13T00:00:00Z
status: implemented
---

# Registry (Corpus)

The typed, hand-maintained map of knowledge on a machine: which directories are knowledge sources, and what layer each belongs to. Spec: `~/Agent/Practice Observability/07 - Feature Specs.md` §Registry; decisions G2-1, G2-9, G2-10; implementation decisions [ADR-0005](../adr/0005-typed-registry-implementation.md).

## Shape

```yaml
machine: example-machine
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
- **Strict validation**: unknown layer, duplicate id (including glob-expanded ids), relative root, or missing fields throw `RegistryFormatException` naming every problem — the registry is load-bearing; it must fail loudly, not rot silently.
- The registry doubles as the **denominator**: the note inventory (all files under all roots) is what "dead" is measured against.

## Implementation

- `src/Kbo/Registry/` — `KnowledgeRegistry` (`Parse`/`Load`/`Resolve`), `KnowledgeSource`, `KnowledgeLayer`, `RegistryLocator`, `RegistryFormatException`
- `src/Kbo/Cli/RegistryCommand.cs` — `kbo registry show`, `kbo registry resolve <path>`
- Tests: `RegistryParseTests`, `RegistryResolveTests`, `RegistryValidationTests`, `RegistryLocationTests`, `RegistryCommandTests`

## Links

- [Schema registry](schema-registry.md) — events carry the `kbroot` field this registry resolves
- [Glossary](glossary.md) — `registry`, `kbroot`, `layer`
