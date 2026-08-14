# Layer: schema registry

**What it does.** Defines the shape of every bronze event: `schemas/<type>/<version>.json` holds one JSON Schema per event type version (the folder IS the registry), `schemas/envelope/1.json` is the shared CloudEvents+extensions contract every type composes, and `schemas/golden/` holds frozen synthetic sample events for every version ever shipped. `EventValidator` (in `src/Kbo/Schemas/`) validates any NDJSON event line against the schema its `schemaref` names.

**What it never does.** It never interprets, filters, or normalizes events (capture stays dumb — P1); it never contains real captured data (golden corpus is synthetic only — G2-9); a schema file is never edited to change meaning — additive optional fields only, anything breaking is a new version plus an upcaster (ADR-0002).

**How to inspect it.**

- `cat schemas/knowledge.read/1.json` — read the contract directly; it's plain JSON Schema
- `cat schemas/golden/knowledge.read.1.ndjson` — see what valid events look like
- `dotnet test` — run the corpus gate locally: every golden event must validate, every schema must have golden coverage, every fixture in `tests/Kbo.Tests/fixtures/broken/` must fail

Details and decisions: [docs/okf/schema-registry.md](okf/schema-registry.md), [ADR-0004](adr/0004-schema-registry-implementation.md).
