# 0007. Harvest: file-granular stateless idempotency, origin markers, hook-only context.loaded

## Status

Accepted (executor decisions within Q4/G2-6; plan step 1.5; owner-confirmed items marked)

## Context

Q4 fixes the miner's role (backfill, gap recovery, verification; hooks primary) and G2-6 fixes that silver prefers harvest values — so bronze legitimately holds both hook and harvest events for one session. Left open: idempotency across re-runs, the dedup unit, payload details, and what harvest cannot honestly reconstruct.

## Decision

1. **Origin markers**: every event carries `data.origin` — `"hook"` (live capture) or `"harvest"` (miner). `data` is schema-open, so this is additive; silver dedups per session preferring harvest (G2-6). *Owner-confirmed 2026-08-12.*
2. **Idempotency is file-granular and stateless**: every harvest event carries `data.transcript` (the transcript file stem); before mining, harvest scans bronze for transcript stems that already have harvest-origin events and skips those files. No ledger beside bronze. *The first cut used session-granularity and double-harvested in production: continuation/compacted transcript files carry a `sessionId` differing from their filename, and up to 31 files can share one session id — the transcript FILE is the honest unit, matching the audit spec's "session files on disk that bronze has never seen".*
3. **One `session.started` per transcript file** (not per session id): dumb and total (P1); files sharing a session id each contribute their own row; silver owns session-level collapsing.
4. **Backfill values**: `model` from the enclosing assistant record (tool events) / first assistant (session); `usage` summed across assistant records deduplicated by `requestId`; `branch` is the transcript's historical `gitBranch` (better than reading today's `.git/HEAD`); search `hits` recomputed authoritatively from the paired `toolUseResult` (`filenames` count preferred over pre-aggregated numbers).
5. **`contenthash` stays null on harvested `knowledge.read`**: the bytes read months ago are unknowable; hashing today's disk under yesterday's timestamp would poison drift detection. Live capture owns hashes.
6. **`context.loaded` is hook-only** (*owner-confirmed 2026-08-12*): implicit loads are not tool activity in transcripts; reconstruction would be guesswork.
7. **Miner robustness**: unparseable transcript lines are skipped; events failing schema validation are dropped and counted, never appended.
8. **Bronze remediation precedent** (*owner-approved 2026-08-12*): the double-harvest duplicates were removed by backup (`bronze-backup-2026-08-12/`) + stripping harvest-origin lines + one clean re-run. Bronze immutability protects captured history from editing — it does not protect defective duplicates whose source (transcripts) still exists; any such surgery requires an explicit backup and owner approval per the decision gate.

## Consequences

Re-running `kbo harvest` any time is safe (verified: second run over 784 transcripts appends nothing). The completeness audit (2.2) gets its unit for free: transcript stems on disk minus `data.transcript` stems in bronze. Silver (1.6) must collapse multi-file sessions and prefer harvest hits over hook best-effort. Backfilled bronze: 784 transcripts → 14,302 events, 0 invalid, ~6.7s.
