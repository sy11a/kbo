# 0031. Configurable task pattern; no task extraction by default

## Status

Accepted (owner decision 2026-08-14, fresh-eye review backlog)

## Context

`GitContext` hardcoded `AC-\d+` as *the* pattern that extracts a task id from
the session's git branch — one org's ticket convention baked into a general
tool, and an origin-project fingerprint in a public repo (it also appeared in
ADR-0001 and the envelope schema). A public tool cannot ship somebody's ticket
convention as universal truth, and there is no meaningful "default" ticket
format to substitute.

## Decision

1. **No task extraction by default.** When no pattern is configured, `task` is
   always `null`. `AC-\d+` is no longer special.
2. The pattern is configured in the typed registry as an optional top-level
   `taskPattern` (a .NET regex; first match in the branch name wins), with a
   `KBO_TASK_PATTERN` environment override taking precedence. This follows the
   architecture rule that nothing org-specific lives outside the adapters and
   the typed registry.
3. An invalid regex fails registry parsing loudly (`RegistryFormatException`),
   like every other registry error. Under live capture the fail-safe contract
   (ADR-0029) turns that into a logged drop, never a broken session.
4. The envelope schema's `task` constraint (`^AC-[0-9]+$` in
   `schemas/envelope/1`) is **relaxed in place** to any non-empty string.
   This is a constraint relaxation, not a reinterpretation: every event ever
   written under the old schema still validates, so bronze replayability
   (ADR-0002's real invariant) is untouched, and the envelope has no version
   field an event could carry — a "v2" would be pure ceremony.

## Consequences

- Public-tool genericity: a fresh install extracts no task ids until its owner
  says what a task id looks like. Adopters with other conventions (`JIRA-\d+`,
  `#\d+`, …) configure theirs.
- The original machine keeps its behavior by adding `taskPattern: AC-\d+` to
  its machine-local registry overlay — bronze continuity, zero event rewrite.
- `task` values in bronze are now only as meaningful as the configured
  pattern; the schema no longer polices their shape beyond non-emptiness.
- ADR-0001's `task` bullet is amended to point here.
