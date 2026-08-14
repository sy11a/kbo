---
type: Component
title: opencode adapter — live capture plugin + SQLite session-store miner
description: TypeScript plugin on tool.execute.after → kbo capture opencode; harvest mines the opencode.db session store; audit covers sessions via a SQLite session source.
tags: [component, adapter, capture, harvest, opencode]
timestamp: 2026-08-12T00:00:00Z
status: implemented
---

# opencode adapter

The second adapter (contract: `03 - Architecture` §Adapters). Session-store format verified on this machine (opencode 1.18.16): sessions/messages/parts in SQLite (`~/.local/share/opencode/opencode.db`), tool activity as `part.data` JSON with `state.input`/`state.metadata`/`state.time`. Implementation decisions: ADR-0014.

## Live capture (plugin, adapter contract #1)

`adapters/opencode/kbo-capture.ts` (installed into `~/.config/opencode/plugins/` with owner approval): hooks `tool.execute.after` (tools `read`/`grep`/`glob`/`write`/`edit`) and session-created events; spawns `kbo capture opencode` detached with a kbo-defined JSON payload on stdin — best-effort, never blocks or fails the session.

| Payload / tool | Event | Notes |
|---|---|---|
| `read` (`args.filePath`) | `knowledge.read` | contenthash per G2-5 |
| `grep`/`glob` (`args.pattern`, `args.path`) | `knowledge.searched` | hits null live; harvest authoritative (G2-6) |
| `write`/`edit` (`args.filePath`) | `knowledge.written` | |
| session start (`directory`) | `session.started` + `context.loaded` | implicit files: global + project `AGENTS.md`; branch from the directory's current `.git/HEAD` (live capture may read the present) |

- **Every opencode event carries `data.transcript` = session id** (hook AND harvest): the session row is the "transcript file" unit, so audit/idempotency reuse the existing stamp machinery unchanged.

## Harvest (`kbo harvest opencode`)

`OpencodeMiner` reads the DB read-only: `session` rows → `session.started` (usage from the pre-aggregated `tokens_*` columns, model from the model JSON's `id`, repo from directory; `branch`/`task` null — the store keeps no branch history, unlike Claude Code transcripts); `part` tool rows → `knowledge.*` with authoritative hits (`metadata.matches`/`count`) and `state.time.start` timestamps. Skips sessions bronze has seen (transcript stamps).

## Audit

`RetentionManifest` gains `SessionDatabase` (a SQLite session source: db path + id/mtime query) — opencode sessions become auditable with zero audit-agent-specific code.

## Implementation

- `src/Kbo/Adapters/Opencode/OpencodeAdapter.cs` + `OpencodeMiner.cs` + payload constants
- `adapters/opencode/kbo-capture.ts` — the plugin
- `src/Kbo/Cli/CaptureCommand.cs` / `HarvestCommand.cs` — gain the `opencode` agent

## Links

- [Claude Code adapter](claude-code-adapter.md) — the pattern this mirrors · [Harvest](harvest.md) · [Audit](audit.md) · [Pulse](pulse.md)
