# BL-500 — kbo without suppressions under the Sy11a.Analyzers policy

**Tier: 0** (direct — a behaviour-preserving cleanup against an external rule set; converge still audits)
**Spec type: bugfix**
Status: converged 2026-10-07 (branch `bl/500-sy11a-no-suppressions`)
Tracker item: [sy11a/Architector#627](https://github.com/sy11a/Architector/issues/627). Follow-up of
[BL-492](../BL-492-sy11a-policy-cleanup/summary.md): the operator's rule since 2026-10-03 is that a policy finding is
never suppressed (`[SuppressMessage]`, `#pragma`, editorconfig severity) but refactored.

## Current, expected, unchanged

**Current.** Under `Sy11a.Analyzers` 0.4.1 (applied from outside, as in BL-492) kbo had 0 active findings, but 11
were silenced in source: CA2100 ×7 (five SQL helpers in `src`, two test fixtures), CA5394 ×2 (ULID randomness,
backoff jitter), CA1031 ×2 (capture's fail-safe catch and its drop log).

**Expected.** No `[SuppressMessage]` anywhere; 0 findings in `src/Kbo` and `tests/Kbo.Tests` under the policy;
`dotnet build` and `dotnet test` green without it.

**Unchanged — the regression contract.** The SQL text sent is the same (only the three `LIMIT` caps are now bound
parameters instead of interpolated constants); output, exit codes, files written and events appended are those of
`master`. ADR-0029 holds for every exception type.

## How it was done

- **CA2100.** Every `CommandText` assignment takes exactly one constant: the `Query` / `Execute` helpers take an
  `Action<DuckDBCommand>` and each caller passes `static command => command.CommandText = """..."""`. The
  `LIMIT {cap}` interpolations became `LIMIT $cap` with a bound parameter. `SqliteSessionSource.IdQuery` (a string, said
  to come from the registry; its only value was a literal in `OpencodeRetention`) became `SetIdQuery`, an
  `Action<SqliteCommand>`. The two test helpers follow the same shape.
- **CA5394.** ULID randomness comes from `IUlidEntropy` (`Fill(Span<byte>)`); production passes
  `CryptographicUlidEntropy.Instance` (`RandomNumberGenerator.Fill`), tests either the same or a counter-based
  `SequenceUlidEntropy` where an id must be reproducible. Backoff jitter uses `RandomNumberGenerator.GetInt32` over
  the same range.
- **CA1031.** `CaptureCommand.Run` catches by type (`IOException`, `UnauthorizedAccessException`, `JsonException`,
  `RegistryFormatException`, `InvalidOperationException`, `FormatException`, `ArgumentException`,
  `RegexMatchTimeoutException`). Any other type reaches a last-chance `AppDomain.UnhandledException` handler,
  registered only at the process entry (`Program.RunCapture`), that logs the drop and exits 0 (ADR-0029, amended).
  `LogDrop` catches `IOException` and `UnauthorizedAccessException`.

Renamed: `Random random` → `IUlidEntropy entropy` on `Ulid.New` and every adapter, miner and job that threads it
(`Random.Shared` → `CryptographicUlidEntropy.Instance`); `SqliteSessionSource.IdQuery` → `SetIdQuery`. The code
snippets in `docs/superpowers/plans/2026-08-14-opencode-skill-capture.md` follow the new signature.

## Converge

- Policy build: 0 errors in `src/Kbo` and `tests/Kbo.Tests` (SARIF, suppressed results excluded, none remain);
  `grep -rn SuppressMessage src tests` is empty.
- `dotnet build` 0 errors; `dotnet test` passes (341: 337 + a malformed-registry capture test and three ULID tests).
- MiniMax wrote the production change, Opus reviewed it (SQL text diffed line by line), a Claude worker wrote the
  tests.

✅ Converged 2026-10-07.
