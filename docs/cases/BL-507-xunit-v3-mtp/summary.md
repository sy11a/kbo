# BL-507 — kbo tests on xUnit v3 / Microsoft.Testing.Platform

**Tier: 0** (direct — test-runner wiring only; no behaviour changes; converge still audits)
**Spec type: feature**
Status: converged 2026-10-10 (branch `bl/507-xunit-v3-mtp`)
Tracker item: [sy11a/Architector#637](https://github.com/sy11a/Architector/issues/637). Prerequisite of the Sy11a
test gate (sy11a_analyzers case 0002 R-010, R-013; rollout tracked in sy11a/sy11a_analyzers#143).

## Current, expected, unchanged

**Current.** `Kbo.Tests` ran on xUnit v2 (`xunit` 2.9.3, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`,
`coverlet.collector`) under VSTest.

**Expected.** `Kbo.Tests` runs on `xunit.v3` 4.0.1 under Microsoft.Testing.Platform, in the canonical shape the
test gate requires: `OutputType=Exe`, the `xunit.v3` package, `global.json` `test.runner = Microsoft.Testing.Platform`,
and none of the MTP properties a lab proved redundant.

**Unchanged.** Production code, test behaviour and asserts, the CI command (`dotnet test --configuration Release
--no-build`), the SDK pin (10.0.106).

## How it was done

- `tests/Kbo.Tests/Kbo.Tests.csproj`: `OutputType=Exe`; the four v2/VSTest packages replaced by `xunit.v3` 4.0.1
  (the version lean-worker uses). `coverlet.collector` is a VSTest data collector; nothing in CI, scripts or docs
  collected coverage, so nothing replaces it.
- `global.json`: `"test": { "runner": "Microsoft.Testing.Platform" }`.
- `GoldenCorpusTests`: v3 theory rows are `TheoryDataRow<...>`, so the row's file name is read as `row.Data.Item1`
  instead of `(string)row[0]`. The only test-code edit.
- CI needed no change: its `dotnet test` call carries no VSTest-only arguments.
- No redundant MTP property was added (`TestingPlatformDotnetTestSupport` and the others listed in the
  sy11a_investigations lab `dotnet-test-governance/lab-mtp-properties.md`).

## Converge

- `dotnet build` 0 warnings, 0 errors (Sy11a.Analyzers 0.6.0 policy active); `dotnet test` 341/341, the same total as
  before the change, also as CI runs it (Release, `--no-build`).
- Test identity: the v2 list (341 lines) and the v3 list (305 lines) hold the same method names; v3 lists a theory
  once instead of once per row (7 theories, 43 rows: 341 − 43 + 7 = 305), and the run executes all 341.
- A deliberately broken assert made `dotnet test` fail with exit code 2 (reverted).
- A Claude worker made the change (test project), Opus reviewed it.

✅ Converged 2026-10-10.
