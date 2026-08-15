# kb-observability — Backlog

Tasks pending implementation. **Rule: update the relevant `docs/okf/` document first, before writing any code.** When a task is fully done, remove it from here.

---

<!-- Last open item from the 2026-08-14 fresh-eye code review (everything else
     from that review is done, including the final polish batch: GitContext
     threading, TreatWarningsAsErrors, ULID de-scope note, and the two
     measure-first items that measured immaterial). -->

## Rebuild throughput: DuckDB Appender

Measured 2026-08-14 while doing the prepared-INSERT item: a 22.8k-event rebuild takes ~16s, and an empty rebuild takes 0.08s — nearly all time is the per-row `ExecuteNonQuery` interop in the insert loop (~0.7ms/row), not statement parsing (preparing once bought only ~4%). If rebuild latency ever matters (it runs hourly via pulse and inside `kbo watch`), the designed fix is DuckDB's Appender API (`DuckDBAppender` in DuckDB.NET) for the bulk load — likely takes the loop to well under a second. Low urgency at current volume.

---

