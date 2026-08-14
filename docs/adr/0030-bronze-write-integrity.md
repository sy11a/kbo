# 0030. Bronze write integrity — concurrent appends and stripped write content

## Status

accepted

## Context

Two coupled defects sat on the append-only capture path (2026-08-14 fresh-eye
review, backlog "Bronze write integrity"):

1. **Concurrent captures could lose events.** `BronzeStore.Append` opened the
   month file with `FileShare.Read`. Claude Code issues parallel tool calls, so
   two `kbo capture` processes can append at the same moment; the second opener
   hit a sharing violation and its event was dropped (recorded only in the
   capture-errors log, ADR-0029). Scanners (`File.ReadLines` in the
   harvest/audit paths) hold `FileShare.Read` handles that block a writer the
   same way.
2. **Write events embedded the full written content.** `knowledge.written`
   events kept `tool_input.content` (and `old_string`/`new_string`/
   `new_source`) verbatim inside `data.raw` — unlike reads, which record
   `contenthash`/`size` per G2-5. This grew bronze without bound and produced
   multi-KB append lines.

Two findings during implementation reshaped the concurrency fix (an 8×25
parallel-append stress test caught both):

- The backlog's prescribed `FileShare.ReadWrite` is **not** a fix: .NET's
  `FileStream` implements `FileMode.Append` as seek-to-end plus *positional*
  writes (`pwrite`), not POSIX `O_APPEND` — two concurrent append handles
  overwrite each other's bytes regardless of line size. The stress test lost
  half its events that way.
- Locking the month file itself (`FileShare.None`) is enforced on Linux via
  advisory locks, but every .NET open of the file holds a shared lock, so an
  exclusive appender and the harvest/audit scanners would block each other —
  a new systematic drop window every hourly pulse.

## Decision

1. Concurrent appenders serialize on a **sidecar lock file** —
   `<events-repo>/.locks/<machine>-<agent>-<month>.lock`, opened
   `FileShare.None` inside a bounded, jitter-backoff retry on `IOException`
   (jitter, because contenders sleeping a fixed interval wake and collide in
   lockstep). The month file itself opens `FileShare.ReadWrite`, so scanners
   — which never touch the lock file — are never blocked on any platform.
   Retry exhaustion surfaces as an `IOException` that the capture fail-safe
   (ADR-0029) records as a drop. The lock directory lives outside the bronze
   tree and `*.lock` is ensured in the events repo's `.gitignore`, keeping
   bronze-git history (ADR-0018) clean.
2. `knowledge.written` events **never embed written content**. The bulky
   free-text fields of `tool_input` (`content`, `old_string`, `new_string`,
   `new_source`) are stripped from `data.raw` and each replaced by a
   `<field>_size` UTF-8 byte count. The live hook additionally records
   `data.contenthash`/`data.size` from the on-disk file it just wrote,
   with the same G2-5 semantics as reads (hash when `kbroot` resolves and the
   file is ≤ 5 MB, size instead above the cap). Harvest-mined writes keep
   `contenthash` null — historical bytes are unknowable, as already accepted
   for mined reads (ADR-0007).

**Bronze stays "sufficient" with path + hash + size.** Full-content
reconstruction from bronze was never a requirement: silver/gold consume only
`subject`/`type`/`time` for written events, and full fidelity lives in the
archived transcripts (ADR-0006). This is additive within `knowledge.written/1`
(`raw` is free-form; `contenthash`/`size` mirror `knowledge.read`), so no
schema version bump. Past bronze lines are never rewritten — immutability
holds; already-captured events with embedded content remain valid.

## Consequences

- Parallel tool calls no longer lose or corrupt capture events: appends are
  mutually excluded cross-process, and readers are never blocked by writers.
- Bronze growth is bounded by activity, not by the size of files being edited.
- Write→read correlation on identical bytes becomes possible on the live path
  (`data.contenthash` now exists on writes as well as reads).
- The verbatim written text is no longer recoverable from bronze — only its
  size/hash. Anyone needing the text goes to the archived transcript.
- Sustained lock contention beyond the retry budget (~1 s) surfaces as a
  recorded drop (ADR-0029) instead of silent loss — accepted residual risk;
  real contention is a handful of sub-millisecond appends.
- The events repo gains a `.locks/` directory (git-ignored). Lock files are
  never deleted — unlinking a held lock would let a late waiter lock the old
  inode while a new writer locks the fresh one, breaking mutual exclusion.
