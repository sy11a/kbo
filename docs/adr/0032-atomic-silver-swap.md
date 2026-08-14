# 0032. Atomic silver rebuild via temp-and-swap

## Status

accepted

## Context

`kbo rebuild` deleted `silver.duckdb` and rebuilt it in place, holding the DuckDB
write lock for the whole derivation (~16s at 22.8k events). `kbo watch`
(ADR-0022) runs that rebuild every tick — 30s by default — so the file was
write-locked more than half the time, and the hourly pulse's `rebuild`,
`report`, and `audit` jobs failed with "Conflicting lock" whenever they landed
in the window (recorded as `job.failed`, red dashboard tiles). Killing watch
mid-rebuild could leave silver with its `events` table but no views, because
views are created last — readers then broke until a clean rebuild. Separately,
every gold reader opened silver with a default read-write connection, so even
two pure readers (watch's dashboard compute vs. pulse's weekly report) took
exclusive locks and could collide with each other.

Options considered: skip the tick when locked (leaves the 16s lock window and
the torn-state-on-kill problem), a cross-process advisory lockfile (most code,
stale-lock handling, still doesn't fix torn state), or removing the contention
structurally by never writing the live file.

## Decision

`SilverRebuilder.Rebuild` derives into a uniquely named temp file
(`silver.duckdb.tmp-<suffix>`) in the same directory, closes the DuckDB
connection (checkpointing and removing the WAL), then atomically renames it
over `silver.duckdb` with `File.Move(..., overwrite: true)`. At the start of
each rebuild, leftover `*.tmp-*` siblings older than one hour are deleted —
the age threshold protects a concurrent rebuild's live temp (a rebuild takes
seconds, not hours).

All gold readers (`DashboardComputer`, `GoldComputer`, `AuditComputer`,
`DailyDigestComputer`) open silver with `ACCESS_MODE=READ_ONLY`, so concurrent
readers share the file instead of excluding each other.

No lockfile, no coordination protocol, no retry loops: contention is removed
structurally. Watch's loop, intervals, and pulse scheduling are untouched.

## Consequences

- The live `silver.duckdb` is never write-locked by a rebuild and never
  observable half-built; concurrent `watch`/`rebuild`/`report`/`pulse` no
  longer conflict. Two concurrent rebuilds each build their own temp and the
  last swap wins — both derive from the same bronze, so either result is
  correct (P3).
- Killing watch mid-rebuild leaves only a stale temp file, cleaned up by the
  next rebuild's age-based sweep; the previous silver stays intact and
  readable. A failed swap likewise fails loudly with the old silver still in
  place — strictly better than delete-first, which left no usable silver.
- A reader holding the old file open across a swap keeps its consistent
  snapshot (POSIX rename semantics); the next open sees the new file.
- Read-only readers can no longer create tables or write by accident — the
  access mode now states the intent the code always had.
- Closes the lock-contention robustness gap flagged against ADR-0022.
