---
type: System
title: kb-observability — Domain Glossary
description: Domain terms mapped to their meaning in this codebase.
tags: [system, glossary, domain]
timestamp: 2026-08-13T00:00:00Z
status: implemented
---

# Domain Glossary

Map internal jargon to what it means in this codebase, so any session edits the right files. Add terms as they emerge; keep meanings current (the okf.md sync rule applies).

| Term | Meaning in this codebase |
|------|--------------------------|
| bronze | Append-only NDJSON event files (`bronze/<machine>/<agent>/<YYYY-MM>.ndjsonl`) in the private `kb-events` repo; immutable, raw agent payload preserved |
| silver | DuckDB tables derived from bronze by `kbo rebuild` (`~/.local/share/kbo/silver.duckdb`); disposable by definition — if rebuild breaks, principle P3 is broken. Shape: `events` + `events_preferred`/`sessions` views (ADR-0008) |
| gold | Report-ready facts computed exactly once per `kbo report` run; emitted as JSON + rendered views; renderers contain zero computation |
| event envelope | CloudEvents 1.0 standard fields plus owner extensions (`machine`, `agent`, `session`, `repo`, `task`, `model`, `kbroot`, `schemaref`) |
| kbroot | Registry root id the event subject falls under; `null` = not knowledge. Registered-root events are the first-class report population (G2-1) |
| registry | Per-machine typed map of knowledge sources (`global`/`framework`/`local`/`skills` layers); doubles as the dead-notes denominator. Machine-local overlay at `~/.config/kbo/registry.yaml`, sanitized example in `registry/`; `kbo registry show/resolve` (ADR-0005) |
| layer | A source's position in the knowledge hierarchy: `global` (the vault), `framework` (reusable KBs), `local` (per-repo), `skills` (agent skill dirs); closed enum, a new layer is a spec change |
| retention manifest | Adapter contract #3: where an agent's transcripts/sessions live on disk (`RetentionManifest` in `src/Kbo/Jobs/`); the archive job and completeness audit iterate manifests, never hardcoded paths |
| adapter | The only agent-specific code: live capture + implicit-loads + retention manifest per agent (Claude Code hook, opencode plugin) |
| harvest | `kbo harvest` — transcript miner producing the same events from agent transcripts; backfill, gap recovery, and authoritative hit counts |
| pulse | `kbo pulse` — runs all due jobs from the job registry via one OS-scheduler entry; every run emits `job.*` events |
| dead-man switch | Health alerting on job *silence*, not errors — a dead job emits nothing; red tile after 3 days (G2-12) |
| tracer bullet | Build order: thinnest end-to-end slice first (capture → bronze → silver → report → ritual), never layers-in-full |
| ritual | Weekly owner review: health panel first, worklists, ≥1 data-driven KB fix, logged in the vault (`~/Knowledge/rituals/`) |
| lens | A deferred analysis feature backfillable from archives (skills, web, reuse, misses) — added when a ritual wants it |
| miss | Derived (never captured) signal that the agent dug for a fact the KB held or should hold; splits into *existed-not-found* vs *did-not-exist* |
| golden corpus | Frozen sample events for every schema version ever shipped (`schemas/golden/`, synthetic only — G2-9); CI fails when any change breaks parsing/upcasting of one |
| schema registry | The `schemas/<type>/<version>.json` folder — one JSON Schema per event type version; the folder IS the registry, embedded into the `kbo` binary at build (ADR-0004) |
| upcaster | Read-time lifter of old event versions to the current shape; breaking change = new version + upcaster |
| theme | Dashboard grouping unit for knowledge usage: registered source id + first path segment under its root (`vault/rituals`); files directly in a root fall under the source id itself (ADR-0017) |
| glob root | Registry root containing `*` as a whole path segment (`~/Repository/*/docs`); expands at load time to one concrete source per matching directory, id `<entry-id>-<matched-dir>` (ADR-0019) |
| daily digest | Per-day Markdown page in `_generated/days/` — sessions, KB/agent usage, searches hit/miss, reads by layer, tokens — for end-of-day review; one page per active day plus an index (ADR-0023) |
| write→read loop | Of notes agents created/edited in the window, the fraction later read (after their first write) — the knowledge flywheel metric (ADR-0027) |
| reach (reuse) | How many DISTINCT sessions read a note — the load-bearing signal, truer than total reads (which a within-session re-read inflates); basis of the reuse/ROI lens (ADR-0026) |
| single-use note | A note read in exactly one session over the window — a review/prune candidate (ADR-0026) |
| content kind | Extension-based classification of a read subject into knowledge (`.md`…), code, config, or other (`ContentKind`), so metrics can separate actual notes from source code that whole-repo registration also captures (ADR-0025) |
| skill.invoked | Event type recording a skill invocation (`data.skill` = name), mined from transcripts (Skill `tool_use`); harvest-only, backfillable retroactively via `harvest --backfill-skills` (ADR-0024) |
