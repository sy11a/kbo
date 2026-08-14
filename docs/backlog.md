# kb-observability — Backlog

Tasks pending implementation. **Rule: update the relevant `docs/okf/` document first, before writing any code.** When a task is fully done, remove it from here.

---

<!-- Items below from the 2026-08-14 fresh-eye code review. Ordered by blast radius:
     data-integrity on the unrebuildable capture path first, then maintainability,
     then genericity/polish.
     (Done so far: capture fail-safe, bronze write integrity, BronzeStore scanner
     collapse, prepared silver INSERT, configurable task pattern.) -->

## Rebuild throughput: DuckDB Appender

Measured 2026-08-14 while doing the prepared-INSERT item: a 22.8k-event rebuild takes ~16s, and an empty rebuild takes 0.08s — nearly all time is the per-row `ExecuteNonQuery` interop in the insert loop (~0.7ms/row), not statement parsing (preparing once bought only ~4%). If rebuild latency ever matters (it runs hourly via pulse and inside `kbo watch`), the designed fix is DuckDB's Appender API (`DuckDBAppender` in DuckDB.NET) for the bulk load — likely takes the loop to well under a second. Low urgency at current volume.

## Review polish (low priority, batch when convenient)

- Thread the already-discovered `GitContext` through `Envelope` instead of re-discovering it per event (`ClaudeCodeAdapter.cs:219`; discovered twice on session start).
- Add `Directory.Build.props` with `TreatWarningsAsErrors` and enable it in CI (`.github/workflows/ci.yml`) — public-repo hygiene.
- Measure `EventValidator` construction cost on the hook (it compiles the whole embedded schema registry per capture process, `CaptureCommand.cs:89`); if material, lazy-load only the needed schema. Perf only.
- `registry.Resolve` is O(sources) linear (`KnowledgeRegistry.cs:22`) and called in tight per-subject loops across ~7 gold methods — memoize/prefix-index only if benchmarking shows it matters; otherwise leave.
- De-scoped by design: the ULID monotonic suffix is unused (ordering relies on file+line order) — harmless, document don't change.

---

