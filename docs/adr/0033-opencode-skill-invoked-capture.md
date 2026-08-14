# 0033. opencode skill.invoked capture

## Status

accepted

## Context

ADR-0024 added `skill.invoked` for Claude Code and scoped opencode out: "its store does not expose skill invocations the same way." That claim is now stale — opencode (verified on 1.18.16's session store) records skill invocations as ordinary tool parts: `part.data.type = "tool"`, `tool = "skill"`, `state.input.name` = the skill name, timestamps in `state.time`. The existing miner pattern applies directly. Sessions already stamped as harvested would never re-emit their skill parts, so history needs the additive-backfill template ADR-0024 established.

## Decision

- **OpencodeMiner** maps `skill` tool parts to `skill.invoked`: subject = skill name, `data` = `{skill, raw, origin: harvest, transcript}` — reusing `skill.invoked/1` unchanged (additive-only P8 untouched).
- **Harvest-only**, mirroring ADR-0024's rationale: the live plugin's `CAPTURED_TOOLS` stays as-is — editing the plugin is a user setup action, and skills data tolerates harvest lag. Silver's `events_preferred` view (ADR-0020) would reconcile a live path if one is ever added.
- **`--backfill-skills` is generalized**: valid on `kbo harvest opencode` too, with the same semantics — skip-set from `BronzeStore.TranscriptsWithType(skill.invoked)`, mined events filtered to `skill.invoked` only. The filter is hoisted so both agent arms share it.

## Consequences

- opencode skills appear in the daily digest "Skills used" and dashboard "Top skills" automatically — gold queries key on `type='skill.invoked'` agent-agnostically.
- One-time `kbo harvest opencode --backfill-skills` recovers the historical invocations back to their original days.
- Slash commands remain uncaptured for both agents — they leave no distinct trace in either store.
- Supersedes ADR-0024's "opencode out of scope" note; the additive-backfill template is now agent-generic.
