# 0041. Doctor degrades gracefully on registry/binary schema skew

## Status

proposed

## Context

2026-08-20 19:02 the registry gained the `sdd:` block (feature commit c4d6e55 landed six
minutes later), while the installed binary was an 2026-08-18 build that did not know the
property. Result: EVERY CLI entrypoint (`report`, `pulse`, `doctor`, hooks) crashed on
`registry is not valid YAML: Property 'sdd' not found …` for seven days. The daily dead-man
surface — the dashboard tiles and, transitively, any watchdog reading them — was itself dead,
and nothing noticed until an operator asked for a weekly report (2026-08-27).

A second, independent failure (2026-08-19) had already silenced harvesting: the `/tmp` tmpfs
quota exhausted restic and SQLite temp writes. Both classes share one property: the tool that
should report the outage was the outage.

## Decision

1. `kbo doctor` MUST NOT require a parseable registry. On a registry parse failure it prints
   a finding — `registry parse broken: <parser message>; hint: binary build older than
   registry feature — rebuild or remove the unknown block` — and exits with a distinct code
   (e.g. 3) instead of crashing before the first finding line.
2. `kbo pulse` runs a registry pre-flight with the same semantics: skip jobs, emit a
   `job.failed` event if possible, never stack-trace.
3. `kbo report` degrades the same way (finding instead of exception).
4. Startup version guard: if the registry file mtime is newer than the binary build stamp,
   doctor notes the skew as a warning (class caught before it bites).
5. Local dead-man coverage lives outside kbo itself (`kbo-canary` hourly timer, installed
   2026-08-27 on fedora-laptop): telemetry-staleness, /tmp pressure, and registry-parse
   probes with desktop notification — the canary keeps working when kbo cannot.

## Consequences

- Config/binary skew becomes a one-line finding with a hint, not a week-long total outage.
- The canary's class-3 probe should move from string-matching `doctor` output to the stable
  exit code once introduced.
- Docs (operations.md) document the exit codes.
