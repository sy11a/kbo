# 0023. Daily digest pages

## Status

accepted

## Context

The owner wanted an end-of-day review surface: per-day pages showing which sessions were active, knowledge-base and agent usage rates, what was searched and what hit vs missed, and skills used — navigable one page per day. Most of this is derivable from existing bronze/silver; "skills used" is not (hooks capture only Read/Grep/Glob/Write/Edit, and skill invocation injects content rather than emitting a tool event). The owner chose to ship the digest from existing data now and add skills capture as a follow-up, with pages as Markdown in the vault (Obsidian-navigable) over HTML.

## Decision

Add `DailyDigestComputer` (gold) + `DailyDigestRenderer`, wired into `kbo report`. For every day with activity in the last 90 days (`WindowDays`), write `_generated/days/YYYY-MM-DD.md` plus `_generated/days/index.md` (a table linking days newest-first via wikilinks). Each page reports sessions (by agent, by repo, KB-touch rate), searches (total/hit/zero-hit + top zero-hit queries), reads by layer, and tokens. Knowledge is classified registry-now (ADR-0021). Renderer computes nothing (P2). Markdown, not HTML, so Obsidian renders and cross-links the pages as daily notes.

Skills are explicitly deferred: the page has no skills section until a `skill.invoked` event type is added and mined retroactively from transcripts (separate task).

## Consequences

- End-of-day review works today: open `days/index.md` or the current day's page; it regenerates on every report (pulse hourly, on demand, and after `watch`'s rebuilds via the next report).
- Same harvest-lag as the dashboard: reads/sessions are live, searches hit/miss and tokens firm up after the next harvest.
- One file per active day (≤90) — small Markdown, committed with the vault; a day's page appears once it has any session, read, or search.
- Engagement paths and search queries appear in these pages exactly as they already do on the dashboard — local vault only, no new exposure.
