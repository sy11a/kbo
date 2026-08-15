# 0034 — Type-aware death conditions and dormant sources

Status: accepted · Date: 2026-08-15

## Context

The 2026-08-14 report listed ~155 dead notes; ~120 were noise: an archived
repo swept in by the registry glob, executed superpowers plans/specs and
journals (lifecycle artifacts), and docs of a project with no activity
since 2026-07-15. A worklist that is 80% noise gets ignored, which defeats
the ritual it exists to serve.

## Decision

1. Registry glob sources accept `exclude: [dirname, ...]`; excluded
   `*`-matched directory names are skipped during glob expansion.
   `exclude` on a non-glob source is a validation error.
2. A note's death condition depends on its role (`NoteRole`): reference
   notes die by non-use; lifecycle artifacts (`/superpowers/plans/`,
   `/superpowers/specs/`, `/journal/` path segments) never enter the dead
   worklist. ADRs stay reference: they are looked up, not executed.
3. Sources with no activity for 21 days (`GoldComputer.DormantAfterDays`)
   are dormant; their dead notes are withheld and reported as a count with
   last-activity date (no-silent-caps). Activity is the newest silver
   event resolving to the source by subject or by repo containment
   (refined by ADR-0035: only usage events — reads and context loads —
   count as activity).

## Consequences

The dead worklist shrinks to genuine anomalies: reference notes in active
sources with zero reads. Withheld categories stay visible as counts and
sections — nothing is silently dropped. Roles are path-based for now;
per-note frontmatter death conditions are a possible future extension,
deliberately out of scope (YAGNI until a ritual asks for it).
