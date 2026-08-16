---
type: Changelog
title: OKF Bundle Changelog
description: Chronological record of significant changes to the OKF knowledge bundle.
tags: [changelog, okf]
timestamp: 2026-08-16T00:00:00Z
---

# OKF Bundle Changelog

## 2026-08-16 — Rebuild bulk-loads via the DuckDB Appender

[silver.md](silver.md) implementation note updated: rows bulk-load through `DuckDBAppender` (one appender per rebuild, flushed on dispose) instead of the per-row prepared `INSERT`. Why: the per-row interop dominated rebuild time — ~16s → ~1.6s on a 23.6k-event store with identical output — and rebuild runs hourly via pulse and inside `kbo watch`. Closes the last fresh-eye-review backlog item; the backlog is now empty.

## 2026-08-16 — Machine-managed role + inventory excludePaths

[first-report.md](first-report.md): new facts row — machine-managed files (`/docs/ai/`, `/adr/template.md`) are counted per source and never on the dead worklist. [registry.md](registry.md): sources accept `excludePaths:` (relative subtrees the inventory skips; resolution unaffected). Glossary rows: machine-managed, excludePaths. Why: the post-ADR-0035 dead list still carried ~24 rows of fleet-law files and tool fixtures — neither is knowledge a ritual should prune; conventions that are fleet-wide live in code (`NoteRole`), repo-specific layout lives in registry config. See ADR-0036.

## 2026-08-15 — Dormancy activity is usage-only

[first-report.md](first-report.md) dormant-sources rule and the glossary's dormant-source row now state that only usage events (`knowledge.read`/`context.loaded`) count as activity — writes alone never wake a source. Why: live verification of ADR-0034 showed a fleet-wide legislator manifest write keeping two genuinely dormant repos "active" (77 noise rows); the backlog finding was resolved the same day by ADR-0035 rather than left to rot. See ADR-0035.

## 2026-08-15 — Report signal-over-noise: type-aware death + dormancy

[first-report.md](first-report.md): dead worklist is now reference-notes-in-active-sources only; new facts rows for lifecycle artifacts (`NoteRole`) and dormant sources; `NoteRole.cs` added to the implementation list. [registry.md](registry.md): glob sources accept `exclude:` (archives skipped at expansion). Glossary rows added: note role, lifecycle artifact, dormant source, glob exclude. Why: the 2026-08-14 report's dead list was ~80% noise (archive sweep, executed plans/specs/journals, a dormant project); the worklist must shrink to genuine ritual candidates without silently dropping anything. See ADR-0034.

## 2026-08-14 — Fresh-eye review polish batch closed

[claude-code-adapter.md](claude-code-adapter.md): git context is discovered once per hook invocation and threaded to every envelope (was re-discovered per event). [schema-registry.md](schema-registry.md): envelope ULID documented as timestamp+randomness only — no monotonic same-millisecond suffix, bronze ordering is file+line append order. Why: the last batch of the 2026-08-14 fresh-eye code review; the two measure-first items (EventValidator construction ~1.5 ms warm / JIT-dominated cold, registry.Resolve ~314 ns/call at real registry size) measured immaterial and were closed without code change.

## 2026-08-14 — Configurable task pattern; no extraction by default (ADR-0031)

[registry.md](registry.md), [claude-code-adapter.md](claude-code-adapter.md), [harvest.md](harvest.md), and the [glossary](glossary.md) updated: the branch → `task` regex is no longer the hardcoded `AC-\d+` but the registry's optional top-level `taskPattern` (env override `KBO_TASK_PATTERN`); when unset, `task` is always `null`. The envelope schema's `task` constraint is relaxed in place to any non-empty string (a pure relaxation — all past bronze still validates). Why: a public tool shouldn't ship one org's ticket convention as default behavior, and `AC-\d+` was an origin-project fingerprint. See ADR-0031; ADR-0001's `task` bullet is amended.

## 2026-08-14 — Bronze scanners collapsed (fresh-eye review backlog)

`BronzeStore`'s four scanners (`HarvestedTranscripts`, `TranscriptsWithType`, `SeenTranscripts`, `LastCompletedJobs`) each re-implemented the same "enumerate month files → parse every line → filter" full-bronze loop; they are now projections over one private `ReadEvents()` iterator that owns the enumerate/parse/skip-malformed-lines behavior. No public surface or behavior change, so no concept doc content changed — file paths and method names referenced by [harvest.md](harvest.md) and [audit.md](audit.md) are untouched. Coverage was added first for the two previously untested scanners (`TranscriptsWithType`, `LastCompletedJobs`) and for malformed-line tolerance across all four. Why: four hand-copied scan loops meant any bronze-format change needed four synchronized edits.

## 2026-08-14 — Bronze write integrity (ADR-0030)

[claude-code-adapter.md](claude-code-adapter.md) and [harvest.md](harvest.md) updated: `knowledge.written` events no longer embed written content — `tool_input`'s `content`/`old_string`/`new_string`/`new_source` are stripped from `raw` and replaced by `<field>_size`; the live hook adds `contenthash`/`size` from the on-disk file per G2-5 (mined writes keep `contenthash` null). `BronzeStore.Append` now serializes concurrent appenders on a git-ignored sidecar lock file (`.locks/<machine>-<agent>-<month>.lock`, exclusive open + jittered bounded retry) while the month file opens share-friendly, so parallel `kbo capture` processes can't overwrite each other and scanners are never blocked. Why: a fresh-eye review flagged the two coupled defects, and a stress test written during the fix showed the prescribed share-mode change was insufficient — .NET appends are positional writes, not `O_APPEND`, so unserialized concurrent appends silently lose data. Bronze stays "sufficient" with path+hash+size. See ADR-0030.

## 2026-08-14 — Capture made fail-safe (ADR-0029)

[claude-code-adapter.md](claude-code-adapter.md) and [pulse.md](pulse.md) updated for the fail-safe capture contract: `kbo capture` never returns non-zero on a runtime failure (bad payload, missing/invalid registry, an event that fails validation, an append error) — it records the drop to `~/.local/state/kbo/capture-errors.log` and exits 0, while valid events in a batch still land in bronze; only genuine CLI misuse exits non-zero. `kbo doctor` now surfaces that log (count + last drop, flagged only when recent). Why: a fresh-eye review found capture returned exit 1 and silently dropped live (unrecoverable) events; the doc already claimed "never breaks a session, errors to a local log" but the code didn't honour it, and drops were surfaced nowhere. Closes the top backlog item.

## 2026-08-14 — Week-over-week deltas (four lenses complete)

[dashboard.md](dashboard.md) gains "This week vs last week" (ADR-0028): KB-touch, failed-search, knowledge reads compared 7d-over-7d with direction-correct green/red arrows. Why: nothing answered "is the practice improving?" directly. Last of the four owner-selected lenses (content-type, reuse, write→read loop, deltas all shipped). Real data on first run: KB-touch 36% (−11pp), failed-search 21% (+2pp), reads −6 — an honest slight dip this week.

## 2026-08-14 — Write → read loop lens

[dashboard.md](dashboard.md) gains the write→read loop (ADR-0027): of notes agents wrote in the window, the fraction later read (after their first write). Why: measures whether agent-produced knowledge actually gets reused — the practice flywheel. Third of four owner-selected lenses. Real data: 62% (590/956) of written notes later read; the top of the list is a frequently-consulted skill definition and an engagement tracking note.

## 2026-08-14 — Reuse / ROI lens

[dashboard.md](dashboard.md) gains "Most-reused knowledge notes" (ADR-0026): notes ranked by distinct-session reach (notes-only via `ContentKind`) plus the single-use ratio. Why: total reads mislead (within-session re-reads inflate); reach is the load-bearing signal, and the single-use tail is the prune list. Second of four owner-selected lenses. Real data: 61% of read notes are single-use; the load-bearing core is a small set of engagement and skill notes.

## 2026-08-14 — Content-type split (knowledge vs code)

[dashboard.md](dashboard.md) gains a "Reads by content type" breakdown (ADR-0025): `ContentKind` classifies read subjects into knowledge/code/config/other. Why: a data probe found ~41% of "knowledge reads" are actually source code (whole-repo registration), inflating every read-metric — this makes the composition visible. First of four owner-selected practice lenses (reuse/ROI, write→read loop, week-over-week deltas to follow).

## 2026-08-14 — Top skills + top zero-hit searches on the dashboard

[dashboard.md](dashboard.md): two ranked lists over the 60-day window — most-used skills (`skill.invoked`) and most-frequent zero-hit search queries. Why: the owner wanted the day-page-only detail (skill names, the query text behind the failed-search rate) on the dashboard. Shared `AppendRankedList` renderer helper.

## 2026-08-14 — Recent-sessions table on the dashboard

[dashboard.md](dashboard.md): the dashboard gains a "Recent sessions" table (last 30, newest first) with per-session reads/searches/skills/writes, KB-touch, tokens. Why: the owner wanted session-level information reachable from the dashboard (their primary surface) rather than only on the day pages. Extracted the registry-now touched-session set as a shared helper feeding both KB-touch and recent-sessions.

## 2026-08-14 — Per-session table on day pages

[daily-digest.md](daily-digest.md): each day page gains a per-session table (time, agent, repo, reads/searches/skills/writes, KB-touch, tokens) beneath the day's totals. Why: the owner wanted to see data per session, not only per-day aggregates; the session-level data already existed in silver.

## 2026-08-13 — Skill capture (skill.invoked)

New `skill.invoked` event type (ADR-0024) mined from Claude Code transcripts' `Skill` tool_use blocks; [harvest.md](harvest.md) documents the mapping and the additive `--backfill-skills` mode, [daily-digest.md](daily-digest.md) flips to status implemented with a "Skills used" section, [claude-code-adapter.md](claude-code-adapter.md) notes the harvest-only mapping, glossary updated. Why: the digest's deferred skills section — transcripts turned out to record skill invocations, so it's recoverable retroactively (backfilled to the earliest transcripts).

## 2026-08-13 — Daily digest pages

New component [daily-digest.md](daily-digest.md) (ADR-0023): `kbo report` writes one Markdown page per active day into `_generated/days/` plus an index — sessions (by agent/repo), KB-touch, searches hit/miss + top zero-hit queries, reads by layer, tokens. Registry-now classification; Obsidian-navigable. Skills-used section deferred (skill capture not yet built — backlog). Glossary gains "daily digest" and the planned "skill.invoked". Why: the owner wanted an end-of-day per-day review of activity and knowledge usage.

## 2026-08-13 — Live dashboard refresh (kbo watch)

[dashboard.md](dashboard.md) documents `kbo watch` (ADR-0022): a foreground loop that rebuilds silver and re-renders the dashboard each tick with a self-reload meta tag. Why: the owner wanted the dashboard live instead of statically generated; chosen as the NOT-list-safe option (no server/daemon, still compute-once per render) over a local live server or SSE streaming, which remain available behind an ADR-0003 amendment if in-flight session watching is ever needed.

## 2026-08-13 — Registry-now chart classification

[dashboard.md](dashboard.md): reads-by-layer and KB-touch now resolve subjects through the current registry at report time (ADR-0021) instead of trusting capture-time `kbroot` stamps — the rule themes already used. Why: the owner's "why is Aug 10–12 empty" question exposed two charts on one page classifying the same events by different rules; the missing days held 12/20/32 local reads that now render.

## 2026-08-13 — Sessions-by-repository provenance table

[dashboard.md](dashboard.md) gains the sessions-by-repository section: full paths from the `repo` envelope field, session counts, agents, last-session date (60-day window). Why: the owner asked to see which folders/repositories the harvested sessions come from — the provenance side of the dashboard.

## 2026-08-13 — Time-bounded harvest preference (lost session tails)

[silver.md](silver.md) and `docs/layer-silver.md` updated: `events_preferred` now drops hook rows only up to the session's last harvest-event time (ADR-0020). Why: the owner's chart question exposed that sessions outliving a daily harvest lost their tail permanently — file-granular dedup never re-mines a stamped transcript, and the old whole-session preference suppressed the tail's hook events. Verified live: today's stamped reads appeared on the layer chart immediately after rebuild.

## 2026-08-13 — Registry glob roots

[registry.md](registry.md) gains glob roots (ADR-0019): `root: ~/Repository/*/docs` expands at load to one source per matching directory (`repo-<dir>` ids), so future repos register themselves. Why: the reads-by-layer investigation showed active knowledge work (OKF/ADR reading in unregistered repos) was invisible, and the owner wanted future repos covered without per-repo registry edits. The machine registry also dropped the three skills roots emptied by the Aug 12 pack retirement.

## 2026-08-13 — Bronze auto-commit (bronze-git job)

[pulse.md](pulse.md) job table gains `bronze-git`: the events repo was git-initialized but never committed, so bronze had no history or tamper-evidence. `VaultGitJob` generalized to `GitCommitJob`, registered for both the vault and the events repo (ADR-0018). Why: the setup health check surfaced the zero-commit repo; the owner chose git history over restic-only.

## 2026-08-13 — Dashboard usability: themes, Russian descriptions, zones

[dashboard.md](dashboard.md) updated for the v2 chart set: new `reads-by-theme.vl.json` (theme = top-level folder under a registered root, ADR-0017) with a never-read list, Russian usage descriptions in every spec's `usermeta.kbo.ru`, green/red threshold zones on the two rate charts, and enriched marks (points, x-zoom, 7-day means). Glossary gains "theme". Why: the owner found the first-week dashboard mute — charts needed to explain themselves and to say *which* parts of the vault are alive.

## 2026-08-12 — Doctor + ritual-surfaced refinements

[pulse.md](pulse.md) gains the doctor section (login-time health check + notification, ADR-0016); `docs/operations.md` added as the owner cheat sheet. The two ritual backlog items shipped: gold read-stats now match by inventory path (dead 93 → 34 on real data) and the audit filters now-registered dirs (20 → 10). Why: the owner asked for a check that cannot be forgotten, and the ritual's false-dead wart would have polluted next week's report.

## 2026-08-12 — Pulse hourly tick (owner-raised scheduling gap)

[pulse.md](pulse.md) updated: hourly dumb tick + calendar-day due-ness from bronze for all jobs. Why: the owner's machine is usually off at 00:00 and a failed run had no same-day retry; one rule (due = no job.completed today) gives catch-up, hourly failure retry, and ~1s no-op ticks with zero new machinery. See ADR-0015.

## 2026-08-12 — opencode adapter implemented (plan step 2.3)

Added [opencode-adapter.md](opencode-adapter.md) (plugin contract, SQLite miner, session-id-as-transcript-unit) and updated [audit.md](audit.md) (SqliteSessionSource) + [pulse.md](pulse.md) (harvest-opencode job). Why: the second agent closes the capture perimeter on this machine — 136 sessions / 6,927 events backfilled, cross-agent model data (glm-5.x) in silver, opencode fully session-auditable. See ADR-0014.

## 2026-08-12 — Vault git implemented (plan step 2.6)

[pulse.md](pulse.md) job table gains `vault-git` (and records `audit`, added in 2.2): the vault is under local git with per-pulse auto-commits, ordered before backup. Why: point-in-time note content is the future judge's substrate (P6 — unbackfillable), `_generated/` history enables the drift metric, and bulk ritual operations become revertible. See ADR-0013.

## 2026-08-12 — Dashboard + health panel implemented (plan step 2.4)

Added [dashboard.md](dashboard.md) (dead-man/last-seen tiles, the four v1 charts, CDN+SRI decision) and the `charts/` row in the codebase map. Why: the see surface exists — pulse's job.* events light the dead-man tiles, and the first trends (failed-search rate 20–60%!) are visible. Rendered by `kbo report` from one gold moment (P2). Verified in a real browser, zero console errors. See ADR-0012.

## 2026-08-12 — Completeness audit implemented (plan step 2.2)

Added [audit.md](audit.md) (seen-transcripts across both origins, manifest SessionFiles, findings-as-gold) and wired the index. Why: the pipeline can now catch its own silent failures — a broken hook shows up as a missing-sessions finding with the harvest recovery command, and the unregistered-sources table nags when the registry map goes stale. Acceptance proven live, including a real gap caught and recovered. See ADR-0011.

## 2026-08-12 — Pulse + scheduler implemented (plan step 2.1)

Added [pulse.md](pulse.md) (job registry, stateless due-ness from bronze, retention manifests, kbo init/systemd) and wired the index; glossary gains a retention-manifest row. Why: the pipeline is now self-sustaining and self-observing (P5) — one daily timer runs harvest→rebuild→archive→backup (+ weekly report), each emitting job.* events the health panel (2.4) will read. Phase 0 scripts replaced per owner approval. See ADR-0010.

## 2026-08-12 — First report implemented (plan step 1.7)

Added [first-report.md](first-report.md) (gold facts, worklist rules, twin outputs) and `docs/layer-gold.md` (P7 card); wired the index. Why: the act surface exists now — dead/hot/stale worklists render wikilinked into the vault with the gold JSON twin beside them; the ritual (1.8) has material. First real run: 541 notes, 164 dead, 20 hot, 0 stale. See ADR-0009.

## 2026-08-12 — Silver + rebuild implemented (plan step 1.6)

Added [silver.md](silver.md) (events table + events_preferred/sessions views, XDG location, full re-derivation) and `docs/layer-silver.md` (P7 layer card); wired the index. Why: gold (1.7) needs a queryable layer that already embodies the G2-6 harvest preference and the multi-file session collapse. P3 proven on real data: delete + rebuild → identical digest over 14,369 events. See ADR-0008.

## 2026-08-12 — Harvest implemented, backfill executed (plan step 1.5)

Added [harvest.md](harvest.md) (miner mapping table, file-granular stateless idempotency via `data.transcript`, hook-only `context.loaded`) and wired it into the index. Bronze store gained the harvested-transcript scan; live events now carry `data.origin: "hook"`. Why: the backfill is the unbackfillable moment — 784 transcripts → 14,302 events landed validated; the audit (2.2) and silver (1.6) build on the origin/transcript markers. See ADR-0007, including the session-vs-file dedup bug found in production and the owner-approved bronze remediation.

## 2026-08-11 — Contract strings consolidated (refactor on step 1.4 branch)

Envelope field names, event type names, event data keys, hook payload keys/tool names/kinds, and env-var names moved into grouped constants: `Kbo.Schemas.EnvelopeFields`/`EventTypes`/`EventDataFields`, `Kbo.Adapters.ClaudeCode.HookPayload`, `Kbo.KboEnvironment`. Why: the same contract literals were duplicated across adapter, bronze store, capture command, and validator; harvest (1.5) will consume the same constants. Tests keep literals deliberately — they are the independent oracle. No behavior change.

## 2026-08-11 — Claude Code adapter implemented (plan step 1.4)

Added [claude-code-adapter.md](claude-code-adapter.md) (hook → `kbo capture claude-code` → bronze; mapping table, raw-minus-tool_response, implicit-context list) and wired it into the index. Why: first live capture ships; the bundle must state exactly what is and is not captured before harvest (1.5) mirrors it. Bronze store and ULID under `src/Kbo/Bronze/` are described there too. See ADR-0006.

## 2026-08-11 — Knowledge registry implemented (plan step 1.3)

Added [registry.md](registry.md) (per-machine YAML overlay, kbroot resolution, `kbo registry` CLI), wired it into the index mapping table, updated the glossary `registry` row and added a `layer` row. Why: step 1.3 shipped the typed registry that every adapter and report will resolve `kbroot` through; the OKF bundle must describe the map before the hook (1.4) starts tagging events with it. See ADR-0005.

## 2026-08-10 — Schema registry implemented (plan step 1.2)

Added [schema-registry.md](schema-registry.md) (envelope + 7 v1 type schemas, golden corpus, `EventValidator`), wired it into the index mapping table, and added glossary rows for `schema registry` and a sharpened `golden corpus`. Why: step 1.2 shipped the first real code; the OKF bundle must describe the contract before harvest builds on it. See ADR-0004 for the implementation decisions.

## 2026-08-10 — Bundle initialized

Initial OKF bundle scaffolded by the Legislator.
