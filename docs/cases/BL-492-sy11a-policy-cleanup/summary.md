# BL-492 — kbo under the Sy11a.Analyzers policy

**Tier: 0** (direct — a behaviour-preserving cleanup against an external rule set; converge still audits)
**Spec type: bugfix**
Status: converged 2026-10-03 (branch `bl/492-sy11a-policy-cleanup`)
Tracker item: [sy11a/Architector#623](https://github.com/sy11a/Architector/issues/623). Part of `sy11a_analyzers` decision 10: every corpus repository is brought to zero findings under the operator's code-quality gate before it takes the package (M5).

## Current, expected, unchanged

**Current.** Built under the `Sy11a.Analyzers` policy (applied from outside through `CustomBeforeMicrosoftCommonProps`; this repository's build files are not touched), kbo reported **2,271** errors: 1,003 in `src/Kbo`, 1,268 in `tests/Kbo.Tests`.

**Expected.** **0** errors in both projects under the policy; `dotnet build` and `dotnet test` green without it.

**Unchanged — the regression contract.**
- Output text and its order, exit codes, exceptions and their messages, files written, events appended, and the order of side effects (clock reads included) are those of `master`.
- Bronze, silver and gold formats and the schema registry are untouched.
- No public surface exists to change: application types became `internal` (CA1515), which a CLI assembly allows.

Three effects are visible, each the policy's intent:
- File lists are sorted ordinally (`Order()` on paths became `StringComparer.Ordinal`), so the audit's 50-transcript cap may pick different transcripts where names differ only in case or culture-sensitive order.
- The registry `taskPattern` regex and the other regexes time out after 1 s instead of never.
- Gold JSON deserialization throws on a `null` in a non-nullable property (`RespectNullableAnnotations`); the four serialize-only option sets also respect required constructor parameters.

## How it was done

`dotnet format` for the mechanical rules (two fixers were dropped: CA1861's, which named fields by duplicate lowercase names, and MA0023's, which broke unnamed groups), then batches of one rule or one folder written by MiniMax and reviewed by Opus, a fix round per review, and method splits (MA0051, CA1502) one or two files at a time. Seven characterization tests were written before the largest splits (the practice mirror and the SDD panel) and pass unchanged. Operator rulings for this repository: CA2100 and CA5394 get justified `[SuppressMessage]` (five SQL helpers; ULID randomness and backoff jitter); MA0224/MA0225 are set on the four serialize-only `JsonSerializerOptions`.

## Converge

- Policy build: 0 errors in `src/Kbo` and in `tests/Kbo.Tests` (SARIF, suppressed results excluded); `dotnet build` 0 errors; `dotnet test` 337 passed (330 + 7 characterization tests).
- `legislator anchors` and `legislator sdd-lint` exit 0; no OKF document anchors a moved file.
- Every review finding (a clock read moved before a `switch`, a `ReportIf` wrapper that hid an `if` from RCS1208, reordered statements) was fixed and re-reviewed.
- The static anchors rung in `docs/ai/engine.py` named by the handoff does not exist in this repository (retired, BL-417); `legislator anchors` is that rung.

✅ Converged 2026-10-03.
