# 0004. Schema registry implementation: JsonSchema.Net, embedded resources, strict envelope

## Status

Accepted (executor decision within ADR-0001/0002; plan step 1.2)

## Context

ADR-0001/0002 fix the envelope and evolution rules but leave library choice, schema packaging, and strictness conventions to the executor (per `08 - Executor Guide`).

## Decision

1. **Validation library**: `JsonSchema.Net` 9.x (json-everything), JSON Schema draft 2020-12. Each type schema composes the envelope via `$ref`; `$id`s use the `https://kb-observability/schemas/<type>/<version>` namespace.
2. **Packaging**: `schemas/**` (excluding `golden/`) is embedded into the `kbo` assembly as resources — the registry travels with the binary and single-file publish keeps working. The folder remains the human-readable source of truth; embedding happens at build from the same files, so they cannot drift.
3. **Strict envelope, open data**: all 15 envelope fields are `required` (nullable ones are present-but-null, never absent) and `additionalProperties: false` — emitter typos and stray fields fail fast. `data` stays open (`additionalProperties: true`): the raw agent payload is preserved untouched (P3) and normalized fields may grow additively.
4. **UTC enforced**: `time` must end in `Z` or `+00:00` (pattern on top of `format: date-time`).
5. **`job.*` events carry no `raw`**: they are self-emitted by `kbo` — the event is the native payload; requiring a copy of itself adds bytes, not information.
6. **Fixture layout**: golden corpus at `schemas/golden/<type>.<version>.ndjson` (part of the registry, synthetic only per G2-9); deliberately-broken fixtures at `tests/Kbo.Tests/fixtures/broken/` (test material, not registry content).

## Consequences

`kbo harvest` (step 1.5) gets validation for free through `EventValidator`. An additive envelope change means editing `envelope/1.json` (new optional field) — old golden events must still pass, which CI enforces. A schema file added without a golden fixture fails the coverage test. Strict `additionalProperties` on the envelope means a *new required* field is by definition a breaking change → new version + upcaster, as ADR-0002 intends.
