# 0008. Silver: DuckDB via DuckDB.NET, XDG location, events table + preference/session views

## Status

Accepted (executor decisions within `03 - Architecture` §Storage and G2-6; plan step 1.6; owner-confirmed items marked)

## Context

The architecture fixes silver as "DuckDB tables derived from bronze by `kbo rebuild`", disposable by definition (P3). Left open: the database's location, the .NET driver, the concrete table/view shape, and how G2-6's "silver prefers harvest values" becomes a query.

## Decision

1. **Location**: `~/.local/share/kbo/silver.duckdb` (*owner-confirmed 2026-08-12*) — XDG data dir, machine-local derived state; never inside kb-events (bronze only), never backed up. Precedence: `--silver` flag → `KBO_SILVER` env → default (same pattern as registry/events).
2. **Driver**: `DuckDB.NET.Data.Full` (bundles native DuckDB + core extensions; survives single-file publish).
3. **Full re-derivation every run**: `kbo rebuild` deletes the file and rebuilds from bronze alone — no incremental state, no migrations; determinism is the P3 proof and is tested (identical logical digest after delete + rebuild, verified on the real 14k-event store).
4. **Shape** (*owner-confirmed 2026-08-12*): one `events` table (envelope fields as typed columns, `time` as TIMESTAMP, `origin`/`transcript` promoted from data, `data` kept whole as JSON text) plus two views gold reads:
   - `events_preferred` — G2-6 concrete: rows survive if harvest-origin, or `context.loaded` (hook-only by ADR-0007), or their session has no harvest coverage. Kills hook/harvest double-counting; harvest hits/model/usage win.
   - `sessions` — one row per session id over preferred `session.started` rows: `min(time)` start, earliest non-null model/branch/task/repo (`arg_min … FILTER`), usage summed across a session's transcript files, `transcript_count`.
5. **Tolerant load**: only `id`/`type`/`time`/`data` are NOT NULL; unparseable bronze lines are counted and skipped, never fatal — rebuild "must always work". Schema validation is emit-time and harvest's job, not rebuild's.
6. **Traceability (P7)**: every view row carries the `events` columns including `id` — any gold number decomposes to bronze event ids; `kbo why` gets this for free.

## Consequences

Gold (1.7) queries only `events_preferred`/`sessions` and inherits the G2-6 preference without re-implementing it. A new lens is a new view — silver has no migration story because it has no persistence story. Real-data rebuild: 14,369 events → 291 sessions in ~10s; the 784 transcript files collapse to true sessions in the view, confirming the multi-file session model from ADR-0007.
