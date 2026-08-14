---
type: System
title: kb-observability — System Overview
description: Root of the OKF knowledge bundle — architecture, tech stack, project layout, and links to every category.
tags: [system, architecture, index]
timestamp: 2026-08-10T19:30:00Z
status: implemented
---

# kb-observability

Practice Observability core — a C# CLI (`kbo`) that captures AI-agent knowledge-usage events into append-only NDJSON bronze, derives DuckDB silver and computed-once gold facts, and renders Markdown worklists into the owner's vault plus a Vega-Lite static dashboard. Requirements are owner-owned and live in `~/Agent/Practice Observability/` (docs 00–09); implementation decisions are ADRs here.

## Tech stack

`.NET`

## What maps to what

| Change | OKF file to update |
|--------|---------------------|
| Event schemas, envelope, golden corpus, validator | `docs/okf/schema-registry.md` |
| Knowledge registry, kbroot resolution, `kbo registry` CLI | `docs/okf/registry.md` |
| Claude Code live capture, hook, bronze store, `kbo capture` | `docs/okf/claude-code-adapter.md` |
| opencode live capture plugin + SQLite miner | `docs/okf/opencode-adapter.md` |
| Transcript mining, backfill, `kbo harvest` | `docs/okf/harvest.md` |
| Silver DuckDB layer, `kbo rebuild` | `docs/okf/silver.md` |
| Gold facts + report renderers, `kbo report` | `docs/okf/first-report.md` |
| Pulse jobs, retention manifests, `kbo pulse` / `kbo init` | `docs/okf/pulse.md` |
| Completeness audit, `kbo audit` | `docs/okf/audit.md` |
| Dashboard, health tiles, chart specs | `docs/okf/dashboard.md` |
| Per-day activity digest pages, `_generated/days/` | `docs/okf/daily-digest.md` |
| Everything else (no other feature slices yet) | `docs/okf/index.md` |

## Changelog

All bundle changes are recorded in [log.md](log.md).
