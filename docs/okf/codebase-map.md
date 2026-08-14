---
type: System
title: kb-observability — Codebase Map
description: Top-level directory map — where things live in this repo.
tags: [system, architecture, map]
timestamp: 2026-08-10T19:30:00Z
status: implemented
---

# Codebase Map

One line per top-level directory. Keep this table in sync with the actual tree (the okf.md sync rule applies): update it when directories are added, removed, or repurposed.

| Directory | What lives there |
|-----------|------------------|
| `adapters/` | Agent-side hook assets (Claude Code: capture script + settings snippet; opencode: capture plugin); the C# mapping code lives in `src/Kbo/Adapters/` |
| `charts/` | Owner-editable Vega-Lite chart specs (`*.vl.json`), embedded into the `kbo` binary at build (ADR-0012) |
| `docs/` | Knowledge: OKF bundle, ADRs, dev journal, backlog, owned AI rules, specs/plans |
| `registry/` | Sanitized example of the per-machine knowledge registry; the real one is a machine-local overlay at `~/.config/kbo/registry.yaml` (ADR-0005) |
| `schemas/` | Event schema registry: one JSON Schema per type version + `golden/` corpus; embedded into the `kbo` binary at build |
| `src/` | Production code — the `Kbo` project builds the `kbo` CLI |
| `tests/` | Test projects — `Kbo.Tests` (xunit) |
