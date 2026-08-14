# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Changed

- `kbo rebuild` is now atomic (ADR-0032): silver derives into a temp file and is renamed over `silver.duckdb`, so the live file is never write-locked by a rebuild and never observable half-built — concurrent `kbo watch`, `rebuild`, `report`, and the hourly pulse no longer fail with DuckDB "Conflicting lock" errors, and killing `watch` mid-rebuild leaves only a swept-up temp file instead of a viewless silver. Gold readers open silver read-only, so concurrent readers share the file.
- Task extraction from git branches is now opt-in configuration (ADR-0031): the envelope's `task` field is populated only when the registry sets a top-level `taskPattern` regex (env override `KBO_TASK_PATTERN`); without one, `task` is always `null`. The previously hardcoded `AC-\d+` convention no longer ships as default behavior — existing installs that want it add `taskPattern: 'AC-\d+'` to their registry overlay. The envelope schema's `task` constraint is relaxed accordingly (any non-empty string; all past events remain valid).
- `kbo rebuild` inserts all rows through a single prepared `INSERT` command (parameters reset per row) instead of building a new command per event. Output is byte-identical; modest throughput gain (~4% on a 22.8k-event store — the remaining per-row cost is DuckDB interop, noted in the backlog).

### Added

- `kbo doctor` surfaces capture-error drops (ADR-0029): the running count and the last drop's date, flagged as a problem only when the most recent drop is within the 3-day dead-man threshold — so silent capture data-loss becomes visible at login instead of hiding in a log no one reads.
- Dashboard "This week vs last week" deltas (ADR-0028): KB-touch, failed-search, and knowledge reads compared 7-day-over-7-day, with green/red arrows correct per metric — the "is the practice improving?" summary. Completes the four practice lenses.
- Dashboard write→read loop lens (ADR-0027): what fraction of agent-written notes were later read — the knowledge flywheel (~62% on real data) — plus the top written-then-read notes.
- Dashboard reuse/ROI lens (ADR-0026): "Most-reused knowledge notes" ranked by distinct-session reach (notes only) plus the single-use ratio — the load-bearing core vs the single-use tail (~61% of notes on real data), for the weekly ritual.
- Dashboard "Reads by content type" breakdown (ADR-0025): registered reads split into knowledge/code/config/other, exposing that a large share of "knowledge reads" is actually source code swept in by whole-repo registration (knowledge ~59% on real data).
- Dashboard "Top skills used" and "Top zero-hit searches" ranked lists (top 15 over the 60-day window) — skill names and the query text behind the failed-search rate, surfaced on the dashboard.
- Recent-sessions table on the dashboard: the last 30 sessions across all repos (date/time, agent, repo, reads/searches/skills/writes, KB-touch, tokens) — session-level detail surfaced on the dashboard itself, not only on the day pages.
- Per-session table on each daily digest day page: one row per session (time, agent, repo, reads/searches/skills/writes, KB-touch, tokens) — the session-by-session view beneath the day's totals.
- Skill capture (ADR-0024): new `skill.invoked` event type mined from Claude Code transcripts (the `Skill` tool), surfaced as a "Skills used" section on the daily digest pages. `kbo harvest claude-code --backfill-skills` recovers skills retroactively from already-harvested transcripts (additive, idempotent), so past days are populated too.
- Daily digest pages (ADR-0023): `kbo report` now writes one Markdown page per active day into `_generated/days/` plus a linked index — sessions active (by agent and repository), KB-touch rate, searches with hit/miss and top zero-hit queries, reads by layer, and tokens — for end-of-day review in Obsidian. (Skills-used section deferred until skill capture lands.)
- `kbo watch [--interval <seconds>]` (ADR-0022): a foreground live-refresh loop that rebuilds silver and re-renders the dashboard on an interval (default 30s, min 5s), with the page self-reloading via a refresh meta tag — removes the manual regenerate/reopen cycle without a server or daemon. Stop with Ctrl-C.
- Dashboard "Sessions by repository" table: full paths of the working folders sessions were started from (60-day window), with session counts, agents, and last-session date — the provenance map of captured data.

### Fixed

- Concurrent bronze appends can no longer lose or corrupt events (ADR-0030): appenders serialize on a git-ignored sidecar lock file (`.locks/` in the events repo, exclusive open + jittered bounded retry) while month files open share-friendly, so parallel tool calls — parallel `kbo capture` processes — can't overwrite each other's appends (.NET appends are positional writes, not `O_APPEND`) and bronze scanners are never blocked by writers.
- `kbo capture` is now fail-safe (ADR-0029): a runtime failure — an unparseable payload, a missing or invalid registry, an event that fails validation, or an append error — is recorded to `~/.local/state/kbo/capture-errors.log` and exits 0, instead of returning non-zero and silently dropping the (unrecoverable) live event. Valid events in a `SessionStart` batch still land in bronze; only genuine CLI misuse (unknown agent/args) exits non-zero.
- Reads-by-layer and KB-touch now classify knowledge by resolving paths through the **current** registry at report time (ADR-0021), matching the theme chart — history fills in retroactively when roots are registered instead of staying frozen to capture-time `kbroot` stamps.
- Silver's `events_preferred` view no longer hides the live tail of long-running sessions (ADR-0020): hook events are dropped only up to the session's last harvest-event time instead of for the whole session, so activity after a daily harvest stays visible in every chart and report the same day.

### Added

- Registry glob roots (ADR-0019): a source root may contain `*` as a whole path segment (`~/Repository/*/docs`), expanding at load time to one source per matching directory (id `<entry-id>-<dir>`), so future repos with a `docs/` folder become knowledge sources automatically.

- Bronze auto-commit (ADR-0018): new daily `bronze-git` pulse job puts the events repo's history under local git (tamper-evidence for append-only bronze), sharing the vault-git implementation (`GitCommitJob`), ordered before backup so restic captures the committed state.

- Dashboard usability (ADR-0017): reads-by-theme chart (theme = top-level folder under a registered root, 60-day window) plus a "Never-read themes" list; Russian what-it-shows/where-to-look descriptions on every chart and tile section (owner-editable via each spec's `usermeta.kbo.ru`); green/red threshold zones on the KB-touch and failed-search charts (owner-tunable in the specs); richer marks everywhere — points, tooltips, x-axis zoom, 7-day moving averages.

- Initial project bootstrap (plan step 1.1): `kbo` .NET 10 CLI solution with xunit tests, ADRs 0000–0003 (decision records, event envelope, schema evolution, NOT-list), event taxonomy registry, AI-development constitution, GitHub Actions CI.
- Schemas v1 (plan step 1.2): JSON Schema registry at `schemas/` — envelope + `knowledge.read/searched/written`, `context.loaded`, `session.started`, `job.completed/failed` — with synthetic golden corpus, `EventValidator`, and CI gates proving both that golden events validate and that broken events are rejected (ADR-0004).
- Doctor (ADR-0016): `kbo doctor [--notify]` checks the pulse timer and every job against the 3-day dead-man threshold; `kbo init` installs it as a login-time service with desktop notifications — the reboot check is automated forever. Owner cheat sheet at `docs/operations.md`.
- Ritual refinements: gold read-stats match by inventory path (historical `kbroot:null` reads of late-registered roots now count; dead list 93 → 34) and the audit's unregistered-sources finding excludes directories now covered by a registered root (20 → 10).
- Pulse scheduling hardened: the systemd timer is now a dumb hourly tick and bronze decides due-ness for every job (once per local calendar day; weekly unchanged) — off-at-midnight machines catch up at power-on and failed jobs retry hourly until they succeed that day (ADR-0015).
- opencode adapter (plan step 2.3): live-capture plugin (`tool.execute.after` → `kbo capture opencode`, kbo-defined payload contract) + `kbo harvest opencode` mining the SQLite session store (pre-aggregated usage, model ids, authoritative search hits; session id doubles as the transcript stamp) + audit coverage via the manifest's new `SqliteSessionSource`; `harvest-opencode` joins pulse; backfill executed (136 sessions, 6,927 events, 0 invalid) (ADR-0014).
- Vault git (plan step 2.6): the vault is under local git — a per-pulse `vault-git` job auto-commits with a fixed message pattern (`kbo auto-commit <ts>`), ordered before backup so restic captures the committed state; `_generated/` history included, giving gold twins version history for free (ADR-0013).
- Dashboard + health panel (plan step 2.4): `kbo report` now also renders `kbo-dashboard.html` + gold twin — dead-man tiles per machine × agent × job (red past 3 days, G2-12), last-seen tiles, and the first chart set (reads by layer, KB-touch rate, failed-search rate, cache-vs-fresh tokens) as owner-editable `charts/*.vl.json` specs; Vega from CDN with pinned SRI hashes, all data inlined (ADR-0012).
- Completeness audit (plan step 2.2): `kbo audit` diffs session transcripts on disk (retention-manifest `SessionFiles`) against stems bronze has seen from either origin, and surfaces `.md` reads under no registered root as unregistered-source candidates; findings render as `kbo-audit.md` + gold twin in `_generated/`; runs weekly from pulse (ADR-0011).
- Pulse + scheduler (plan step 2.1): `kbo pulse` runs harvest→rebuild→archive→backup every pulse and report weekly (due-ness read statelessly from bronze `job.*` events, P5); manifest-driven transcript archive (zstd, Phase-0-compatible layout, opencode SQLite consistent copy + ISO-week snapshots) and restic backup replace the Phase 0 scripts; `kbo init` registers the daily systemd user timer (`Persistent=true`) and disables the old timers (ADR-0010).
- First report (plan step 1.7): `kbo report` computes gold once (P2) — dead notes (M=30/N=60), hot notes (top 20), staleness (≥3 reads/60d, unmodified >90d) over the registry inventory + silver reads — and renders the wikilinked Markdown worklist plus the gold JSON twin into `~/Knowledge/_generated/` (ADR-0009).
- Silver layer (plan step 1.6): `kbo rebuild` derives a disposable DuckDB database (`~/.local/share/kbo/silver.duckdb`, `KBO_SILVER`/`--silver` override) from bronze alone — full `events` table plus `events_preferred` (G2-6 harvest-over-hook preference) and `sessions` (multi-transcript collapse, usage sums) views; P3 proven by identical digest after delete + rebuild (ADR-0008).
- Harvest (plan step 1.5): `kbo harvest claude-code` mines transcripts into the same validated envelope events — model + usage + historical-branch backfill, authoritative search hit counts (G2-6), file-granular stateless idempotency via `data.origin`/`data.transcript` markers; full backfill executed (784 transcripts, 14,302 events, 0 invalid) (ADR-0007).
- Claude Code adapter, live capture (plan step 1.4): async PostToolUse/SessionStart hook → `kbo capture claude-code` mapping Read/Grep/Glob/Write/Edit/NotebookEdit to `knowledge.*` events and session start to `session.started` + `context.loaded`; kbroot tagging, contenthash (G2-5), best-effort search hits (G2-6); validated-on-emit appends into the auto-created local `~/Repository/kb-events` bronze store (ADR-0006).
- Typed knowledge registry (plan step 1.3): per-machine YAML overlay at `~/.config/kbo/registry.yaml` (`KBO_REGISTRY`/`--registry` override) mapping source roots to `kbroot` ids with strict validation and longest-root resolution; `kbo registry show` / `kbo registry resolve <path>` CLI; sanitized example at `registry/example.yaml` (ADR-0005).

### Changed

- `knowledge.written` events no longer embed the written content (ADR-0030): `tool_input`'s `content`/`old_string`/`new_string`/`new_source` are stripped from `data.raw` and replaced by `<field>_size` byte counts, and live-captured writes now carry `contenthash`/`size` of the on-disk file just written (same G2-5 semantics as reads; harvest-mined writes keep `contenthash` null). Bronze growth is bounded by activity instead of the size of files being edited, and small append lines are what keep concurrent appends intact. Past bronze lines are untouched.

### Fixed

### Removed
