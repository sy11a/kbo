# 0024. skill.invoked capture (retroactive from transcripts)

## Status

accepted

## Context

The daily digest deferred a "skills used" section because skill usage wasn't captured (ADR-0023): the live hook matches only Read/Grep/Glob/Write/Edit, and invoking a skill injects content rather than emitting a matched tool event. Investigation showed Claude Code transcripts **do** record skill invocations as `tool_use` blocks with `name: "Skill"` and `input.skill` — so the data is recoverable retroactively via the harvest miner, not merely forward-only.

## Decision

Add an additive `skill.invoked` event type (v1 schema + golden corpus; schema evolution is additive-only per P8):

- **subject** = the skill name; **data** = `{ skill, raw, origin, transcript }`.
- **TranscriptMiner** emits `skill.invoked` for the `Skill` tool (harvest-origin). Newly harvested transcripts get skills automatically.
- **Retroactive backfill**: `kbo harvest claude-code --backfill-skills` re-mines all transcripts, keeps only `skill.invoked`, and skips transcripts already carrying one (`BronzeStore.TranscriptsWithType`) — additive, idempotent, never duplicating other event types (which the normal transcript-stamp dedup would otherwise re-emit).
- **Daily digest** gains a "Skills used" section (per-day skill → count).

Not added: a live Skill hook. That would require editing the user's hook matcher in `settings.json` (a setup action) and only covers future sessions; the harvest miner already captures skills with the usual daily lag, so live capture is deferred as unnecessary. opencode skill capture is also out of scope here (its store does not expose skill invocations the same way).

## Consequences

- Skills appear on all day pages, past and present — the one-time `--backfill-skills` run recovered them back to the earliest transcripts.
- `skill.invoked` is harvest-only, so it inherits the harvest lag (a day's skills firm up after the next harvest) — consistent with searches/tokens.
- A broken-fixture test that used `skill.invoked/1` as its example of an *unknown* schemaref was repointed to a genuinely nonexistent ref.
- The additive-backfill pattern (`--backfill-skills`, `TranscriptsWithType`) is now the template for any future mined event type added after transcripts are already harvested.
