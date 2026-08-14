---
type: Component
title: Claude Code adapter — live capture into bronze
description: PostToolUse/SessionStart hook mapping Claude Code tool activity to envelope events, kbroot-tagged, appended to the kb-events bronze store.
tags: [component, adapter, capture, bronze, claude-code]
timestamp: 2026-08-14T00:00:00Z
status: implemented
---

# Claude Code adapter

The first adapter (adapter contract: `03 - Architecture` §Adapters). Agent-specific code lives only here; everything else goes through the [registry](registry.md) and the [schema registry](schema-registry.md). Implementation decisions: ADR-0006.

## Flow

```
Claude Code ──PostToolUse/SessionStart hook (bash, async)──▶ kbo capture claude-code (stdin JSON)
    ▶ map tool payload → envelope event (kbroot via registry, task via git branch, contenthash per G2-5)
    ▶ validate against schema registry
    ▶ append NDJSON to ~/Repository/kb-events/bronze/<machine>/claude-code/<YYYY-MM>.ndjsonl
```

## Mapping

| Hook / tool | Event | subject | Notes |
|---|---|---|---|
| PostToolUse Read | `knowledge.read` | file path | contenthash when kbroot != null and ≤ 5 MB, else size (G2-5) |
| PostToolUse Grep/Glob | `knowledge.searched` | pattern | root = `tool_input.path` else cwd; hits best-effort from tool_response (G2-6) |
| PostToolUse Write/Edit/NotebookEdit | `knowledge.written` | file path | Edit/NotebookEdit are writes (owner-confirmed 2026-08-11); written content is never embedded — `contenthash`/`size` from the on-disk file per G2-5, stripped `tool_input` fields replaced by `<field>_size` (ADR-0030) |
| Skill (harvest only) | `skill.invoked` | skill name | mined from transcripts, not the live hook (Skill isn't in the hook matcher); `data.skill` = the invoked skill (ADR-0024) |
| SessionStart | `session.started` + `context.loaded` per implicit file | session id / path | implicit files: global CLAUDE.md, project CLAUDE.md, `.claude/rules/*.md`, auto-memory MEMORY.md |
| other tools | none | — | capture stays file-tool scoped in v1 |

- `raw` preserves the hook payload **minus `tool_response`** (owner-confirmed 2026-08-11): full fidelity lives in archived transcripts; harvest recomputes hit counts authoritatively. On write events, `tool_input`'s free-text fields (`content`, `old_string`, `new_string`, `new_source`) are additionally stripped and replaced by `<field>_size` byte counts — bronze stays "sufficient" with path+hash+size and its growth is bounded by activity, not file sizes (ADR-0030).
- `task`: first `AC-\d+` of the cwd's git branch, read from `.git/HEAD` directly (no subprocess); raw branch in `data.branch` on `session.started`.
- Best-effort by design: the hook never blocks or breaks a session. Two layers guarantee it — the wrapper script backgrounds `kbo` and exits 0 immediately, and `kbo capture` itself never returns non-zero on a *runtime* failure (unparseable payload, missing/invalid registry, an event that fails validation, or an append error). It records the drop to `~/.local/state/kbo/capture-errors.log` and exits 0; any events that *do* validate still land in bronze. Only a genuine CLI misuse (wrong agent/args) exits non-zero. `kbo doctor` surfaces that log so silent drops stay visible (ADR-0029).

## Implementation

- `src/Kbo/Adapters/ClaudeCode/` — payload mapping
- `src/Kbo/Bronze/` — event store append (creates + git-inits kb-events on first use); concurrent appenders serialize on a git-ignored sidecar lock file (`.locks/<machine>-<agent>-<month>.lock`, exclusive open + jittered retry — .NET appends are positional writes, not `O_APPEND`), while the month file itself opens share-friendly so scanners are never blocked (ADR-0030)
- `src/Kbo/Cli/CaptureCommand.cs` — `kbo capture claude-code` (stdin → bronze)
- `adapters/claude-code/` — hook script + registration snippet
- Retention manifest (adapter contract #3) arrives with the archive job (step 2.x)

## Links

- [Registry](registry.md) · [Schema registry](schema-registry.md) · [Glossary](glossary.md)
