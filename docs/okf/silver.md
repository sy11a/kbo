---
type: Component
title: Silver — DuckDB derived layer + kbo rebuild
description: kbo rebuild derives the disposable DuckDB silver layer from bronze alone (P3); events table plus the origin-preference and session-collapse views gold reads.
tags: [component, silver, duckdb, rebuild]
timestamp: 2026-08-12T00:00:00Z
status: implemented
---

# Silver (DuckDB) + `kbo rebuild`

Silver is disposable by definition: `kbo rebuild` re-derives it from bronze alone, every time — if that breaks, P3 is broken. Implementation decisions: ADR-0008. Location: `~/.local/share/kbo/silver.duckdb` (`--silver` flag → `KBO_SILVER` env → XDG default); never inside kb-events (bronze only) and never backed up.

Rebuild is atomic (ADR-0032): it derives into a `silver.duckdb.tmp-<suffix>` sibling, closes the connection, then renames over the live file — so the live silver is never write-locked by a rebuild, never observable half-built, and concurrent `watch`/`pulse`/`report` runs don't conflict. Leftover temp files (from a killed rebuild) are swept by the next rebuild once older than an hour. Gold readers open silver with `ACCESS_MODE=READ_ONLY` so concurrent readers share the file.

## Shape (v1)

- **`events` table** — full fidelity: every envelope field as a typed column (`time` as TIMESTAMP), `origin`/`transcript` extracted from data, `data` kept whole as JSON. Every row traceable to its bronze line by `id` (P7).
- **`events_preferred` view** — the G2-6 rule made concrete, **time-bounded** (ADR-0020): for sessions with harvest coverage, hook/live rows of the types harvest also produces (`knowledge.*`, `session.started`) are dropped **only up to the session's last harvest-event time**; hook rows newer than that (the live tail of a session that outgrew its harvest) stay visible. Harvest values (authoritative hits, model, usage) win where harvest ran; `context.loaded` rows always survive (hook-only by ADR-0007).
- **`sessions` view** — one row per session id over preferred `session.started` rows: earliest start, first non-null model/branch/task/repo, usage sums across a session's transcript files, transcript count.

Gold (`kbo report`, step 1.7) reads only the views.

## Implementation

- `src/Kbo/Silver/SilverRebuilder.cs` — derive into temp + atomic swap
  (DuckDB.NET, ADR-0032). All rows insert through a single parameterized
  `INSERT` command created once per rebuild — DuckDB.NET caches the prepared
  statement while `CommandText` is unchanged, so only parameter values reset
  per row.
- `src/Kbo/Cli/RebuildCommand.cs` — `kbo rebuild [--silver <file>] [--events-repo <dir>]`
- `docs/layer-silver.md` — P7 layer card (what it does / never does / how to inspect)

## Links

- [Harvest](harvest.md) — supplies the origin/transcript markers the views rely on
- [Schema registry](schema-registry.md) · [Claude Code adapter](claude-code-adapter.md)
