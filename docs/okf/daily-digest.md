---
type: Component
title: Daily Digest — per-day activity pages (end-of-day review)
description: kbo report renders one Markdown page per day into the vault — sessions active, KB/agent usage, searches hit/miss, reads by layer, tokens — plus a linked index.
tags: [component, daily-digest, gold, report, obsidian]
timestamp: 2026-08-13T00:00:00Z
status: implemented
---

# Daily Digest (`kbo report` → `_generated/days/`)

Per-day review surface (ADR-0023): for every day with activity in the last `WindowDays` (90), one Obsidian-navigable Markdown page plus an `index.md` linking them newest-first. Computed once from silver (P2); the renderer contains zero computation. Regenerates on every `kbo report` — pulse (hourly), on demand, and under `kbo watch`'s dashboard loop indirectly via the next report — so today's page firms up through the day.

## Each day page

- **Sessions** — count active that day, broken down by agent and by repository (the `repo` envelope field), the KB-touch rate, and a **per-session table**: one row per session (time, agent, repo, reads · searches · skills · writes, KB-touch, tokens in/cache).
- **Skills used** — skills invoked that day with counts, mined from transcripts as `skill.invoked` events (ADR-0024); backfilled retroactively so past days are populated too.
- **Knowledge searches** — total, hits, zero-hits, miss rate, and the top zero-hit queries (the "notes to write / rename" list).
- **Knowledge reads** — total registered reads and a by-layer breakdown.
- **Tokens** — fresh input vs cache-read, summed over the day's sessions.

## Classification & lag

- **Registry-now** (ADR-0021): reads/touch resolve subjects through the current registry, not the capture-time `kbroot` stamp — history fills in when roots are registered.
- Reads and session counts are live (hook events); **searches hit/miss and tokens are harvest-lagged** (authoritative hits and usage arrive with the daily harvest), so the current day's search/token numbers firm up after the next harvest.

## Links

- [Dashboard](dashboard.md) — the trend/health surface; the digest is its per-day companion
- [Silver](silver.md) — `sessions` / `events_preferred` views the digest reads
