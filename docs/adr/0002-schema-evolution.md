# 0002. Additive-only schema evolution with upcasters and a golden corpus

## Status

Accepted (owner decision, requirements `04 - Event Schema` / P4)

## Context

Bronze events are immutable and live forever. Any schema change that reinterprets old events lies about history; any silent breaking change destroys replayability.

## Decision

1. New fields are optional with a default. Fields are never renamed or repurposed.
2. A breaking change is a new event-type version (e.g. `knowledge.read/2`) plus an upcaster in `upcasters/` that lifts old events to the new shape at read time. Old bronze lines are never rewritten.
3. `schemas/<type>/<version>.json` (JSON Schema) is the registry — the folder IS the registry; one file per type version.
4. Golden corpus: frozen sample events for every version ever shipped live in this repo (synthetic only — never copied from real captured data, per G2-9). CI validates all fixtures and fails the moment any change breaks parsing or upcasting of any golden event; a deliberately-broken fixture test proves the gate works.

## Consequences

Schema changes are cheap when additive and deliberately expensive when breaking. History stays truthful. The golden corpus makes "does old data still parse?" a CI fact instead of a hope.
