# kb-observability — Backlog

Tasks pending implementation. **Rule: update the relevant `docs/okf/` document first, before writing any code.** When a task is fully done, remove it from here.

---

## Harden `kbo watch` vs silver lock contention

`kbo watch` holds the DuckDB write-lock on `silver.duckdb`; a concurrent `kbo rebuild`/`report`/`pulse` (e.g. the hourly timer) fails with a "Conflicting lock" error, and killing watch mid-rebuild can leave silver without its views (recovered by a clean `kbo rebuild`). Options: watch should skip a tick if silver is locked, or the pulse/watch should coordinate, or watch could rebuild into a temp DB and swap. Low urgency (single-user, watch is foreground) but a real robustness gap in ADR-0022.

## opencode skill capture

Claude Code skills are captured (ADR-0024); opencode skill/command invocations are not (its session store doesn't expose them the same way). If it becomes worthwhile, extend `OpencodeMiner` to emit `skill.invoked` and backfill. Low priority — Claude Code is where skills predominantly run.

---

