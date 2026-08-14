# 0001. Event envelope: CloudEvents 1.0 + owner extensions

## Status

Accepted (owner decision, requirements `04 - Event Schema`; clarified by `09 - Decision Record` G2-1/G2-4/G2-5)

## Context

Events are the permanent raw layer (bronze) of Practice Observability. The schema must be owned, not adopted, yet built on named practices so evolution is managed rather than felt out (`09 - Decision Record` Q3).

## Decision

One NDJSON line per event: CloudEvents 1.0 standard fields (`specversion`, `id`, `source`, `type`, `time`, `subject`, `data`) plus owner extensions (`machine`, `agent`, `session`, `repo`, `task`, `model`, `kbroot`, `schemaref`).

- `id`: ULID (sortable). `source`: `//<machine>/<agent>`. `time`: UTC ISO-8601.
- `data` always preserves the raw agent-native payload untouched, alongside normalized fields (P3).
- Capture is total (G2-1): every file-tool event is emitted; `kbroot` is set by registry lookup, `null` outside registered roots. Reports treat `kbroot != null` as the first-class population.
- `task`: first `AC-\d+` match from the session cwd's git branch (captured raw in `data`), else `null` (G2-4). Unbackfillable, so captured from day one (P6).
- `contenthash`: SHA-256 of file bytes truncated to 16 hex chars, computed only on `knowledge.read`/`context.loaded` events with `kbroot != null`; files over 5 MB are not hashed (size recorded instead) (G2-5).
- `schemaref`: `<type>/<version>` into the schema registry (`schemas/<type>/<version>.json` in this repo).

## Consequences

Bronze is self-describing and replayable; `kbo rebuild` needs nothing but bronze. Every future lens reads the same envelope. Validation happens on emit where possible, always on harvest, and in CI against the golden corpus (ADR-0002).
