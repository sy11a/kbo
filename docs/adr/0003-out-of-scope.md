# 0003. Out of scope — the NOT-list

## Status

Accepted (owner decision, requirements `05 - Principles`; referenced there as "ADR-0003 in the tooling repo")

## Context

Meta-work displacement is the project's biggest named risk. Scope is guarded by an explicit list of things deliberately not built; revisiting an item is a decision, not drift.

## Decision

Not built unless a real, named need arrives and the owner agrees:

- custom storage engine
- web server
- resident daemon / custom service
- OTel collector
- real-time anything
- cross-machine reports in v1
- judge layer before the gate (`06 - Implementation Plan` Phase 4)
- config UI (files are the UI)
- second charting technology
- editing the requirement docs by an executor

## Consequences

Every "wouldn't it be nice" hits this list first. Changes to this ADR are owner decisions by definition.
