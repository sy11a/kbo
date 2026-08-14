---
type: Component
title: Silver — DuckDB derived layer + kbo rebuild
description: kbo rebuild derives the disposable DuckDB silver layer from bronze alone (P3); events table plus the origin-preference and session-collapse views gold reads.
tags: [component, silver, duckdb, rebuild]
timestamp: 2026-08-12T00:00:00Z
status: implemented
---

# Silver (DuckDB) + `kbo rebuild`

Silver is disposable by definition: `kbo rebuild` deletes and re-derives it from bronze alone, every time — if that breaks, P3 is broken. Implementation decisions: ADR-0008. Location: `~/.local/share/kbo/silver.duckdb` (`--silver` flag → `KBO_SILVER` env → XDG default); never inside kb-events (bronze only) and never backed up.

## Shape (v1)

- **`events` table** — full fidelity: every envelope field as a typed column (`time` as TIMESTAMP), `origin`/`transcript` extracted from data, `data` kept whole as JSON. Every row traceable to its bronze line by `id` (P7).
- **`events_preferred` view** — the G2-6 rule made concrete, **time-bounded** (ADR-0020): for sessions with harvest coverage, hook/live rows of the types harvest also produces (`knowledge.*`, `session.started`) are dropped **only up to the session's last harvest-event time**; hook rows newer than that (the live tail of a session that outgrew its harvest) stay visible. Harvest values (authoritative hits, model, usage) win where harvest ran; `context.loaded` rows always survive (hook-only by ADR-0007).
- **`sessions` view** — one row per session id over preferred `session.started` rows: earliest start, first non-null model/branch/task/repo, usage sums across a session's transcript files, transcript count.

Gold (`kbo report`, step 1.7) reads only the views.

## Implementation

- `src/Kbo/Silver/SilverRebuilder.cs` — delete + derive (DuckDB.NET)
- `src/Kbo/Cli/RebuildCommand.cs` — `kbo rebuild [--silver <file>] [--events-repo <dir>]`
- `docs/layer-silver.md` — P7 layer card (what it does / never does / how to inspect)

## Links

- [Harvest](harvest.md) — supplies the origin/transcript markers the views rely on
- [Schema registry](schema-registry.md) · [Claude Code adapter](claude-code-adapter.md)
