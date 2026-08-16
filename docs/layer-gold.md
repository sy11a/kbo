# Layer: gold (P7 card)

**What it does** — `kbo report` computes every number exactly once (P2): scans the registry roots for the note inventory (the denominator), queries silver's `events_preferred` for read stats, and derives the dead/hot/stale worklists into a `GoldReport`. Two renderers emit it: `~/Knowledge/_generated/kbo-report.md` (wikilinked worklist, the *act* surface) and `kbo-report.gold.json` (the machine-readable twin).

**What it never does** — put computation in a renderer; read bronze directly (silver is its only event source); write anywhere but `_generated/`; persist state between runs (every run recomputes from scratch).

**How to inspect** —
- `kbo report` prints dead/hot/stale/inventory counts.
- The JSON twin is the exact fact set the Markdown renders — diff them conceptually and they must agree, because both come from one `GoldReport`.
- Trace a number: worklist rows carry paths; read events for a path: `SELECT id, time, session FROM events_preferred WHERE subject = '<path>' AND type IN ('knowledge.read','context.loaded');` — then grep the ULID in bronze.
- Thresholds (M=30/N=60, stale ≥3 reads/>90d, hot top-20, dormant >21d) are constants in `GoldComputer` — ADR-0009, ADR-0034.
- The dead worklist is type- and activity-aware (ADR-0034): lifecycle artifacts (plans/specs/journals, `NoteRole`) never enter it; sources with no activity in 21 days are dormant and their dead notes are withheld as a counted section; registry glob sources can `exclude:` directories (e.g. archives) from inventory entirely.
