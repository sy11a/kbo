# 0005. Typed registry implementation: YAML overlay in XDG config, longest-root resolution, `kbo registry` CLI

## Status

Accepted (executor decision within the §Registry spec and G2-1/G2-9/G2-10; plan step 1.3)

## Context

`07 - Feature Specs` §Registry fixes the registry shape (per-machine YAML: `machine` + `sources` with `id`/`layer`/`root`) and G2-9 fixes that the real registry is a machine-local overlay with only a sanitized example committed. Left to the executor: the overlay's concrete location, the YAML library, resolution semantics for nested roots, validation strictness, and how "kbo loads it" is made observable.

## Decision

1. **Overlay location**: `~/.config/kbo/registry.yaml` (XDG config, machine-local by nature; survives repo re-clones). Precedence: `--registry <file>` flag → `KBO_REGISTRY` env var → the XDG default. The repo commits `registry/example.yaml` (sanitized) only. *Owner confirmed 2026-08-11.*
2. **YAML library**: YamlDotNet (the de-facto .NET standard; YAML is fixed by the spec, so a YAML dependency is unavoidable).
3. **Resolution semantics**: a path resolves to the source whose root contains it; with nested roots (per-repo `local` root containing a `framework` KB) the **longest matching root wins**. Prefix matching is segment-safe (`/home/a/Knowledge` does not claim `/home/a/KnowledgeBackup`), ordinal, no symlink resolution — the registry lookup stays a dumb bound (G2-5 rationale).
4. **Strict validation, fail loud**: missing `machine`/`sources`, missing source fields, unknown layer, duplicate id, or relative root throw `RegistryFormatException` naming every problem — the registry is hand-maintained and load-bearing; silent tolerance would rot the map.
5. **Observable surface**: `kbo registry show` (print machine + sources) and `kbo registry resolve <path>` (print the `kbroot` id or `null`) — the plan's acceptance is demonstrable from the shell, and adapters (step 1.4) reuse the same `KnowledgeRegistry.Load`/`Resolve`.
6. **Layers are a closed enum** (`global | framework | local | skills`) matching the spec; a new layer is a spec change, not a config typo to tolerate.

## Consequences

Step 1.4's hook tags `kbroot` by calling the same `Resolve` — no path logic outside the registry (P-constraint: nothing path-specific hardcoded outside adapters and the typed registry). The note-inventory denominator (dead-notes report) will enumerate files under `Sources[].Root`. Unknown-layer tolerance being zero means adding a `framework` KB on a machine requires only editing the overlay — but a typo'd layer blocks loading until fixed, which is the intended nag. `registry/<machine>.yaml` in-repo (the spec's illustrative path) is not used; the example file documents the overlay location.
