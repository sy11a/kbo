# 0016. Doctor: login-time health check with desktop notification

## Status

Accepted (owner request 2026-08-12: "add auto check and notify me — I'm afraid I will forget")

## Context

The reboot acceptance check (ADR-0010) and the dead-man panel both require the owner to *look*. The owner asked for an automatic check with a notification, so nothing depends on remembering.

## Decision

1. **`kbo doctor [--notify]`**: checks (a) `kbo-pulse.timer` is active and (b) every job known to bronze has a `job.completed` within the dead-man threshold (3 days, G2-12). Exit 0 healthy / 1 problems; `--notify` sends a desktop notification via `notify-send` — critical with the problem list, or a short normal-urgency "healthy" so the owner sees the system is alive without opening anything.
2. **`kbo init` installs `kbo-doctor.service`** (`WantedBy=default.target`): the check runs at every login — which is exactly the moment after a reboot the owner feared forgetting. The one-time reboot acceptance is thereby automated forever.
3. Doctor reads bronze and systemd only — no new state; it is a viewport on the same dead-man facts the dashboard tiles show (P5, computed the same way).

## Consequences

A dead timer or a 3-day-silent job now surfaces as a critical desktop notification at login, not as a red tile waiting to be seen. `notify-send` absence degrades silently (the check still prints and exits nonzero). The two ritual-surfaced backlog refinements shipped in the same change: gold read-stats match by inventory path (historical `kbroot:null` reads of late-registered roots count — dead list 93 → 34 on real data) and the audit's unregistered-sources finding filters directories now covered by a registered root (20 → 10).
