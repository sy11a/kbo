# kb-observability — Backlog

Tasks pending implementation. **Rule: update the relevant `docs/okf/` document first, before writing any code.** When a task is fully done, remove it from here.

---

## Harden `kbo watch` vs silver lock contention

`kbo watch` holds the DuckDB write-lock on `silver.duckdb`; a concurrent `kbo rebuild`/`report`/`pulse` (e.g. the hourly timer) fails with a "Conflicting lock" error, and killing watch mid-rebuild can leave silver without its views (recovered by a clean `kbo rebuild`). Options: watch should skip a tick if silver is locked, or the pulse/watch should coordinate, or watch could rebuild into a temp DB and swap. Low urgency (single-user, watch is foreground) but a real robustness gap in ADR-0022.

## opencode skill capture

Claude Code skills are captured (ADR-0024); opencode skill/command invocations are not (its session store doesn't expose them the same way). If it becomes worthwhile, extend `OpencodeMiner` to emit `skill.invoked` and backfill. Low priority — Claude Code is where skills predominantly run.

---

<!-- Items below from the 2026-08-14 fresh-eye code review. Ordered by blast radius:
     data-integrity on the unrebuildable capture path first, then maintainability,
     then genericity/polish. Do the silver INSERT item before the rest.
     (Done so far: capture fail-safe, bronze write integrity, BronzeStore scanner collapse.) -->

## Prepare the silver INSERT once

`SilverRebuilder.InsertEvent` builds a fresh `DuckDBCommand` and re-parses the `INSERT` text for every event inside the transaction (`SilverRebuilder.cs:148`). Prepare the statement once outside the loop and reset parameters per row. Correctness unchanged (`RebuildResult` identical); straight throughput win on `rebuild`.

## Configurable task pattern (genericity)

`GitContext` hardcodes `AC-\d+` as *the* branch task pattern (`GitContext.cs:11`) — one org's ticket convention baked into a general tool, and a faint origin-project fingerprint in a public repo (also in `docs/adr/0001` and `docs/okf/claude-code-adapter.md`). Add an optional `taskPattern` to the registry with a `KBO_TASK_PATTERN` env override. **Decided (2026-08-14): no task extraction by default** — when `taskPattern` is unset, `task` is always `null`; a public tool shouldn't ship one org's ticket convention. `AC-\d+` becomes opt-in via config. Update the two docs (`docs/adr/0001`, `docs/okf/claude-code-adapter.md`) and note it in the new ADR.

## Review polish (low priority, batch when convenient)

- Thread the already-discovered `GitContext` through `Envelope` instead of re-discovering it per event (`ClaudeCodeAdapter.cs:219`; discovered twice on session start).
- Add `Directory.Build.props` with `TreatWarningsAsErrors` and enable it in CI (`.github/workflows/ci.yml`) — public-repo hygiene.
- Measure `EventValidator` construction cost on the hook (it compiles the whole embedded schema registry per capture process, `CaptureCommand.cs:89`); if material, lazy-load only the needed schema. Perf only.
- `registry.Resolve` is O(sources) linear (`KnowledgeRegistry.cs:22`) and called in tight per-subject loops across ~7 gold methods — memoize/prefix-index only if benchmarking shows it matters; otherwise leave.
- De-scoped by design: the ULID monotonic suffix is unused (ordering relies on file+line order) — harmless, document don't change.

---

