# 0039 — Service sessions are marked and excluded from practice metrics

Status: accepted · Date: 2026-08-17

## Context

On 2026-08-16 two automation waves ran inside registered knowledge roots:
the legislator fleet rollout (seven headless `opencode run` upgrades) and
a batch of ~30 scripted sessions in one engagement repo. The next report
showed local-layer reads at 601/day (typical: 20–50), the KB-touch rate
jumped into the green band, two dormant sources woke (their withheld dead
notes flooding the worklist from 11 to 93 rows), and the recent-sessions
table was wall-to-wall automation.

None of that measured *practice*. A fleet-upgrade agent touches
registered knowledge because touching registered knowledge IS its job —
counting it inflates every lens the weekly ritual reads, which is exactly
the Goodhart failure the Vision names ("reads measure discoverability,
not value"). Fleet rollouts and batch runs are now routine, so this is a
standing distortion, not a one-off.

## Decision

1. **Vocabulary.** A *practice session* is a human-driven working session
   (the thing the four lenses exist to observe). A *service session* is a
   run whose purpose is maintaining or operating the system itself: fleet
   upgrades, batch/scripted sweeps, migrations, recovery harvests.
2. **Marking at launch, via agent identity — no schema and no harvest
   change.** Service runs launch under a dedicated opencode agent whose
   name carries the `service-` prefix (e.g. `opencode --agent
   service-fleet`). The opencode session store records that agent, and
   the miner *already* carries it into bronze as the `session.started`
   event's `data.agent_mode` — so the mark is present end-to-end with
   zero capture changes. The envelope's `agent` field keeps meaning the
   adapter (`opencode`), so dead-man and last-seen are untouched by
   construction. Silver derives two views: `service_sessions` (session
   ids whose `agent_mode LIKE 'service-%'`) and `practice_events`
   (`events_preferred` minus those sessions). Launchers own the marking:
   `fleet.sh` passes it always; other automation adopts the same prefix.
   An unmarked session counts as practice — the only workable default,
   with the known cost that a forgotten flag inflates metrics (loudly
   visible in recent-sessions) rather than hiding work.
3. **Metric semantics.** Practice lenses — KB-touch, reads-by-layer,
   failed-search, reuse, write→read, week-over-week, hot/dead/stale
   usage, and source activity for dormancy (tightening ADR-0035: usage
   means *practice* usage) — count practice sessions only. Service
   events stay first-class everywhere else: bronze (append-only, as
   always), dead-man tiles, last-seen (service agents get their own
   tile), day pages.
4. **No silent caps.** The dashboard states the exclusion: "N service
   session(s) excluded from practice metrics this window", with the
   agent identities listed.
5. **The past stays as recorded.** Pre-ADR service sessions (2026-08-16)
   are unmarked and remain counted — bronze is append-only and post-hoc
   reclassification by cwd/time heuristics is exactly the fragility this
   ADR avoids. The 08-16 spike is read around, not rewritten.

## Alternatives rejected

- **A `service: true` envelope field** — a schema change plus validator
  churn for information the `agent` field already carries; past events
  would need a migration bronze forbids.
- **Post-hoc classification (cwd, prompt text, session shape)** — a
  heuristic that silently misfiles both directions; classification
  belongs at launch time where the intent is known.
- **Counting service reads as practice deliberately** ("an agent is a
  consumer too") — defensible for discoverability research, fatal for
  the ritual's kill-switch metrics: a rollout week would always look
  like a great practice week.

## Consequences

- `fleet.sh` (legislator repo) gains `--agent service-fleet` on its
  `opencode run` line — one-line change, done alongside implementation.
- Silver queries that feed the practice lenses gain
  `agent NOT LIKE 'service-%'`; dead-man/last-seen queries stay
  unfiltered.
- Dormancy regains its meaning: a paused project stays dormant through a
  fleet rollout, so the dead-note worklist stops yo-yoing (11→93 on
  2026-08-16) with maintenance activity.
- New automation must remember the prefix; the recent-sessions table
  makes a forgotten flag visible the same day.
