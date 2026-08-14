# 0014. opencode adapter: session-id as transcript unit, plugin payload contract, SQLite session source

## Status

Accepted (executor decisions within the adapter contract and plan step 2.3; session-store format verified on this machine, opencode 1.18.16)

## Context

The adapter contract fixes: live capture via a TypeScript plugin on opencode's tool-execution hooks, `context.loaded` for implicit loads (AGENTS.md), a retention manifest, and "verify its session-store format there". Verified: sessions/messages/parts in SQLite; `session` rows pre-aggregate usage/model/agent-mode; `part.data` JSON carries tool calls with `state.input`, `state.metadata` (authoritative `matches`/`count`), and `state.time` (ms epochs); no branch history is stored.

## Decision

1. **`data.transcript` = session id on every opencode event, hook and harvest**: the session row is this agent's "transcript file" unit, so harvest idempotency (`HarvestedTranscripts`) and the audit reuse the existing stamp machinery with zero new concepts. (Claude Code keeps file stems — its files and sessions are not 1:1; opencode's are.)
2. **Plugin payload is a kbo-defined contract** (`hook_event_name`: `tool.execute.after` | `session.start`; `session_id`, `directory`, `tool`, `args`): the plugin (`adapters/opencode/kbo-capture.ts`) is a dumb forwarder — spawns `kbo capture opencode` detached, filters to `read`/`grep`/`glob`/`write`/`edit`, never throws. Mapping intelligence lives in C# (`OpencodeAdapter`), where it is tested.
3. **Live search hits are null** (opencode's after-hook exposes args, not results); harvest recomputes authoritatively from `state.metadata` (G2-6 pattern, same as Claude Code).
4. **Harvest `branch`/`task` are null**: the store keeps no branch history (the `workspace.branch` column is empty in practice) and stamping today's HEAD onto old sessions would fabricate history. Live capture reads the current `.git/HEAD` legitimately. `model` comes from the session row's model JSON `id`; usage from the pre-aggregated `tokens_*` columns.
5. **Audit via `SqliteSessionSource`**: `RetentionManifest` gains a declarative session-database entry (db path + id/mtime query) — opencode sessions became auditable with no agent-specific audit code; any future SQLite-store agent reuses it.
6. **Implicit loads**: global `~/.config/opencode/AGENTS.md` + project `AGENTS.md` (this repo's CLAUDE.md is a symlink to AGENTS.md, so vault/repo instructions resolve identically).
7. **Pulse gains `harvest-opencode`** as its own every-pulse job (own dead-man tile, per machine × agent × job as the health panel expects).

## Consequences

Acceptance (mirror of 1.4/1.5) met: backfill 136 sessions → 6,927 events, 0 invalid, ~4s; rerun appends nothing; audit reports opencode complete. Cross-agent data landed immediately (glm-5.x model era; 300 kbroot-tagged events including the roots registered at the first ritual). Plugin installation into `~/.config/opencode/plugins/` is an owner action (setup gate), mirroring the Claude Code hook decision (ADR-0006 §8).
