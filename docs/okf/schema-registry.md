---
type: Component
title: Schema registry — event envelope + v1 event types
description: JSON Schema registry (`schemas/<type>/<version>.json`), the envelope contract, golden corpus, and the C# validator that enforces them.
tags: [component, schemas, bronze, validation]
timestamp: 2026-08-10T00:00:00Z
status: implemented
---

# Schema registry

The folder `schemas/` at the repo root IS the registry ([ADR-0002](../adr/0002-schema-evolution.md)): one JSON Schema (draft 2020-12) file per event type version, `schemas/<type>/<version>.json`. The envelope contract ([ADR-0001](../adr/0001-event-envelope.md)) lives at `schemas/envelope/1.json` and every type schema composes it via `$ref`.

## v1 contents

| Schema | Constrains |
|---|---|
| `envelope/1` | CloudEvents 1.0 fields + owner extensions (`machine`, `agent`, `session`, `repo`, `task`, `model`, `kbroot`, `schemaref`); ULID `id`; UTC ISO-8601 `time` |
| `knowledge.read/1` | `data`: `path`, nullable `contenthash` (16-hex SHA-256 prefix, G2-5), nullable `size`, `raw` |
| `knowledge.searched/1` | `data`: `pattern`, nullable `root`, nullable `hits` (hook best-effort, harvest authoritative — G2-6), `raw` |
| `knowledge.written/1` | `data`: `path`, `raw` |
| `context.loaded/1` | `data`: `path`, nullable `contenthash`, nullable `size`, `raw` |
| `session.started/1` | `data`: nullable `branch` (raw git branch — G2-4), nullable `usage` (incl. `cache_read` vs fresh input tokens), `raw` |
| `job.completed/1` | `data`: `job`, `duration_ms` (self-emitted by `kbo` — no `raw`) |
| `job.failed/1` | `data`: `job`, nullable `duration_ms`, `error` (no `raw`) |

`skill.invoked` and `web.searched/fetched` are reserved in v1 (see [taxonomy registry](../events.md)) — no schema files until they ship.

## Validation

`src/Kbo/Schemas/EventValidator.cs` loads every schema in the registry (embedded into the `kbo` assembly at build — [ADR-0004](../adr/0004-schema-registry-implementation.md)) and validates one NDJSON event line against the schema named by its `schemaref`, returning `EventValidationResult`. Used by tests now; `kbo harvest` (plan step 1.5) validates on ingest through the same code. Library: JsonSchema.Net 9.x, draft 2020-12. Envelope is strict (`additionalProperties: false`, all fields required, nullables present-but-null); `data` is open so raw payloads pass untouched.

## Golden corpus

`schemas/golden/<type>.<version>.ndjson` — frozen synthetic sample events (never real captured data, G2-9) for every version ever shipped. Tests assert every golden event validates AND every schema version has golden coverage. Deliberately broken fixtures live in `tests/Kbo.Tests/fixtures/broken/` and must FAIL validation — proving the CI gate can actually reject.

## How to inspect

- `cat schemas/knowledge.read/1.json` — the contract, human-readable JSON Schema
- `cat schemas/golden/knowledge.read.1.ndjson` — what a valid event looks like
- `dotnet test` — runs the corpus gate locally, same as CI
