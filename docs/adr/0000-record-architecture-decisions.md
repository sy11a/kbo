# 0000. Record architecture decisions

## Status

Accepted

## Context

We need to record the architectural decisions made on this project so that future contributors (human or AI) understand why the codebase looks the way it does, not just what it looks like.

## Decision

We will use Architecture Decision Records, as described by Michael Nygard, and follow the format in `docs/adr/template.md`. Decisions are numbered sequentially and never renumbered or deleted — superseded decisions get a new ADR that references the old one.

Numbering note: this bootstrap ADR is 0000 because the owner's requirements package pins ADR-0001…0003 to specific decisions (envelope, schema evolution, the NOT-list — see `06 - Implementation Plan` step 1.1 and `05 - Principles`).

## Consequences

Every non-trivial architecture decision gets a permanent, dated record. See `docs/ai/rules/core/adr.md` for when to write one.
