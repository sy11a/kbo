# kb-observability — Backlog

Tasks pending implementation. **Rule: update the relevant `docs/okf/` document first, before writing any code.** When a task is fully done, remove it from here.

---

<!-- Last open item from the 2026-08-14 fresh-eye code review (everything else
     from that review is done, including the final polish batch: GitContext
     threading, TreatWarningsAsErrors, ULID de-scope note, and the two
     measure-first items that measured immaterial). -->

## Dead-list residual noise: machine-managed law files and tool fixtures

Found 2026-08-15 reading the post-ADR-0035 dead list (35 rows). ~11 rows are genuine candidates (RKruiterApi's unread ADRs/OKF docs). The rest are two categories the worklist still misclassifies as reference knowledge: (1) legislator-managed law files — `docs/ai/rules/**` (11 rows in ProxyController) and `docs/adr/template.md` scaffolding — machine-written artifacts nobody "reads" as notes; (2) tooling data swept in by whole-dir registration of the legislator skill root — `evals/benchmarks/**`, `evals/fixtures/**`, `.superpowers/**` (12 rows). Candidate fixes to decide deliberately: registry sub-path excludes (extend ADR-0034's `exclude` to path segments), or new `NoteRole` categories (machine-managed / fixture), or narrowing the legislator source root. Not patched ad hoc.

---

## Rebuild throughput: DuckDB Appender

Measured 2026-08-14 while doing the prepared-INSERT item: a 22.8k-event rebuild takes ~16s, and an empty rebuild takes 0.08s — nearly all time is the per-row `ExecuteNonQuery` interop in the insert loop (~0.7ms/row), not statement parsing (preparing once bought only ~4%). If rebuild latency ever matters (it runs hourly via pulse and inside `kbo watch`), the designed fix is DuckDB's Appender API (`DuckDBAppender` in DuckDB.NET) for the bulk load — likely takes the loop to well under a second. Low urgency at current volume.

---

