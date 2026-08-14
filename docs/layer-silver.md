# Layer: silver (P7 card)

**What it does** — `kbo rebuild` derives `~/.local/share/kbo/silver.duckdb` from bronze alone, every run from scratch: an `events` table (one row per bronze line, envelope fields typed, `data` preserved as JSON), an `events_preferred` view (G2-6, time-bounded per ADR-0020: harvest rows win over hook rows up to the session's last harvest-event time, the newer hook tail stays visible; `context.loaded` always survives), and a `sessions` view (one row per session id, usage summed across its transcript files).

**What it never does** — persist anything bronze doesn't contain; mutate or validate bronze; compute report numbers (that's gold, P2); get backed up (it's disposable — if deleting it loses anything, P3 is already broken).

**How to inspect** —
- `kbo rebuild` prints event/session counts and skipped-line count.
- Any DuckDB client: `SELECT type, count(*) FROM events GROUP BY type;`
- Trace a number: every view row carries the `events` columns including `id` — grep that ULID in `kb-events/bronze/**/*.ndjsonl` to see the raw event.
- Prove P3 any time: note counts, delete the file, `kbo rebuild`, compare.
