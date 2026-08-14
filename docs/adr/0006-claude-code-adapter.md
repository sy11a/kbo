# 0006. Claude Code adapter: async bash hook → `kbo capture`, raw without tool_response, Edit counts as written

## Status

Accepted (executor decisions within the adapter contract of `03 - Architecture` and G2-1/G2-4/G2-5/G2-6; plan step 1.4; owner-confirmed items marked)

## Context

The adapter contract fixes: live capture via async PostToolUse bash hook, `context.loaded` at session start, agent-specific code confined to the adapter. Left open: hook mechanics, payload retention detail, tool coverage interpretation, bronze store location, and deployment.

## Decision

1. **Hook mechanics**: one bash script (`adapters/claude-code/kbo-capture.sh`) for both `SessionStart` and `PostToolUse` (matcher `Read|Grep|Glob|Write|Edit|NotebookEdit`). It reads stdin, hands the payload to `kbo capture claude-code` in a detached background process (`setsid … &`), and **always exits 0 immediately** — capture can never block or fail a session. Errors land in `~/.local/state/kbo/hook.log`, and gaps are harvest's job to recover (G2-6 pattern: hooks best-effort, miner authoritative).
2. **`raw` = hook payload minus `tool_response`** (*owner-confirmed 2026-08-11*): for Read, `tool_response` duplicates the full file content into bronze on every read. Full fidelity lives in the archived transcripts; the hook only parses `tool_response` live for the best-effort search hit count, then drops it.
3. **Edit and NotebookEdit map to `knowledge.written`** (*owner-confirmed 2026-08-11*): an executor interpretation of G2-1's "all file-tool events (Read/Grep/Glob/Write)" — an edit is a write in every report question that consumes `knowledge.written`.
4. **Bronze store**: `~/Repository/kb-events` (*owner-confirmed 2026-08-11*), `KBO_EVENTS_REPO` override. `kbo` creates and `git init`s it on first append. Appends are single whole-line `O_APPEND` writes; month file selected from the event's own `time` (`bronze/<machine>/<agent>/<YYYY-MM>.ndjsonl`).
5. **Envelope details**: `task`/`repo`/`branch` read from `.git/HEAD` directly (walking up from `cwd`, worktree `gitdir:` pointers followed) — no subprocess on the hook path. `model` is `null` live (the hook payload has no model); harvest fills it from transcripts. `time` is second-precision UTC; ordering finer than that comes from the ULID's millisecond timestamp.
6. **Validation on emit**: every mapped event passes `EventValidator` before append; an invalid event is an error (logged, exit 1), never a silently-written line.
7. **Implicit context files** (v1 list): `~/.claude/CLAUDE.md` (global-instructions), `<cwd>/CLAUDE.md` (project-instructions), `<cwd>/.claude/rules/*.md` (rules), and the auto-memory `MEMORY.md` under `~/.claude/projects/<munged-cwd>/memory/` — each existing file emits one `context.loaded` with `raw.kind`.
8. **Deployment**: `dotnet publish` single-file to `~/.local/bin/kbo`; the hook script resolves it via `KBO_BIN` (default `~/.local/bin/kbo`). Hook registration is a snippet (`adapters/claude-code/settings-hooks-snippet.json`) merged into `~/.claude/settings.json` by the owner — never auto-installed.

## Consequences

A broken registry or missing binary silences capture without breaking sessions — exactly the failure mode the completeness audit (step 2.2) exists to flag. Backfilling `model` and authoritative hit counts is harvest's contract (step 1.5). The implicit-context list is a hardcoded adapter detail by design (adapters are the sanctioned agent-specific place); extending it (e.g. `@import`ed rule files) is an adapter change, not a schema change.
