# 0029. Capture is fail-safe — drop, log, never fail the session

## Status

accepted

## Context

`kbo capture` runs as a Claude Code `PostToolUse`/`SessionStart` hook (and an
equivalent opencode plugin). The wrapper script (`adapters/claude-code/kbo-capture.sh`)
already backgrounds the binary and exits 0, so a slow or crashing capture cannot
block the agent. But the C# command itself returned a non-zero exit and dropped
the event on any runtime hiccup: a registry that isn't set up yet, an unparseable
payload, an event that fails schema validation. Two problems follow:

1. **Silent data loss.** A dropped live event is unrecoverable — unlike harvest,
   there is no transcript to re-mine it from (P6). The only trace was raw stderr
   dumped into the wrapper's `hook.log`, mixed with everything else and surfaced
   nowhere. You could be losing capture events for weeks without knowing.
2. **A fragile contract.** The exit-1 behaviour was only harmless because *that
   specific wrapper* discards it. Any other wiring — a synchronous hook, the
   opencode plugin, a future integration — would surface kbo's internal error
   into the observed session. Observation must never perturb the observed.

The okf adapter doc already *claimed* the hook "never blocks or breaks a session
(errors to a local log)"; the code did not honour it.

## Decision

`kbo capture` never returns non-zero on a **runtime** failure. On an unparseable
payload, a missing/invalid registry, an event that fails validation, or an append
error, it appends one line — `<utc-iso>\t<agent>\t<reason>` — to
`~/.local/state/kbo/capture-errors.log` and exits 0. Events that *do* validate
still land in bronze (mirroring harvest's append-valid/log-invalid behaviour), so
one bad event in a `SessionStart` batch never sinks the others. An unsupported
hook event for a known agent is a benign no-op (exit 0, no log), like an untracked
tool. Only genuine **CLI misuse** — an unknown agent or malformed arguments —
still exits non-zero, because that is a setup mistake to fix, not a per-event
runtime condition.

The sidecar lives under `~/.local/state/kbo/` (XDG state, matching the wrapper's
`hook.log`), not `~/.local/share/`. `kbo doctor` reads it and reports the running
drop count plus the last drop's date, flagging a problem only when the most recent
drop is within the 3-day dead-man threshold — a stale count is informational, a
fresh one is actionable.

## Consequences

- Silent capture data-loss becomes visible: `doctor` (and its login notification)
  now surface fresh drops, closing the gap between "the KB looks quiet" and "capture
  has been broken since you changed your registry."
- The fail-safe contract holds regardless of how capture is wired, not only under
  the shipped wrapper.
- The log is append-only and never rotated; the total count grows unbounded. That
  is acceptable — the actionable signal is *recency*, not the total — but log
  rotation is a future nicety if the file ever grows large.
- Bronze immutability and sufficiency (P3) are untouched: this changes only whether
  an event is written and whether a drop is recorded, never past lines.
