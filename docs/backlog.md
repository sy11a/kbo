# kb-observability — Backlog

Tasks pending implementation. **Rule: update the relevant `docs/okf/` document first, before writing any code.** When a task is fully done, remove it from here.

---

<!-- Last open item from the 2026-08-14 fresh-eye code review (everything else
     from that review is done, including the final polish batch: GitContext
     threading, TreatWarningsAsErrors, ULID de-scope note, and the two
     measure-first items that measured immaterial). -->

## Dormancy: machine maintenance writes count as activity

Found 2026-08-15 while verifying ADR-0034 on live data: `repo-CareerPlatform` and `repo-RKruiter_TestClient` should be dormant (no human/agent work since 2026-07-15 / 2026-07-17), but a single fleet-wide legislator run on 2026-08-05 wrote `docs/ai/manifest.json` in every repo, and that `knowledge.written` event counts as source activity — so both sources stayed "active" and kept their 77 dead rows on the worklist (112 instead of ~35). Missed category: machine-generated maintenance writes (legislator manifest regeneration, and any future fleet-wide stamp) are not evidence a project is alive. Candidate fixes to decide deliberately (ADR): exclude machine-managed paths (`docs/ai/**`) from the activity query, or exclude `knowledge.written` events whose subject is a machine-managed file, or classify activity by event origin. Not patched ad hoc per the plan's no-silent-caps discipline.

---

## Rebuild throughput: DuckDB Appender

Measured 2026-08-14 while doing the prepared-INSERT item: a 22.8k-event rebuild takes ~16s, and an empty rebuild takes 0.08s — nearly all time is the per-row `ExecuteNonQuery` interop in the insert loop (~0.7ms/row), not statement parsing (preparing once bought only ~4%). If rebuild latency ever matters (it runs hourly via pulse and inside `kbo watch`), the designed fix is DuckDB's Appender API (`DuckDBAppender` in DuckDB.NET) for the bulk load — likely takes the loop to well under a second. Low urgency at current volume.

---

