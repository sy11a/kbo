# BL-503 — kbo consumes Sy11a.Analyzers from GitHub Packages

**Tier: 0** (direct — build wiring only; no behaviour changes; converge still audits)
**Spec type: feature**
Status: converged 2026-10-08 (branch `bl/503-sy11a-analyzers-package`)
Tracker item: [sy11a/Architector#631](https://github.com/sy11a/Architector/issues/631). Follow-up of
[BL-500](../BL-500-sy11a-no-suppressions/summary.md) and sy11a_analyzers R-070, which publishes the package to GitHub
Packages from 0.5.0 on.

## Current, expected, unchanged

**Current.** The `Sy11a.Analyzers` policy reached kbo only from outside the repository (a `policy.props` injected
with `CustomBeforeMicrosoftCommonProps`, BL-492 and BL-500); kbo's own build and CI ran without it.

**Expected.** kbo's build references `Sy11a.Analyzers` `[0.5.0]` itself, so every local and CI build enforces the
policy: 0 findings today, any new finding fails the build.

**Unchanged.** Source, tests, output and behaviour; only the build wiring moves.

## How it was done

- `nuget.config` (new): `nuget.org` and the GitHub source `https://nuget.pkg.github.com/sy11a/index.json`, with
  package source mapping so only `Sy11a.*` comes from GitHub. No credentials in the repository.
- `Directory.Build.props`: `<PackageReference Include="Sy11a.Analyzers" Version="[0.5.0]" PrivateAssets="all" />`
  for both projects (the package applies its test-project config to `tests/Kbo.Tests` itself).
- `ci.yml`: `permissions: contents: read, packages: read`; a step passes `GITHUB_TOKEN` as the `github` source's
  password. The package grants `sy11a/kbo` Read under "Manage Actions access".
- `global.json` (new) pins the SDK to 10.0.106 (`latestPatch`), and CI's `setup-dotnet` reads it. Unpinned, CI took
  SDK 10.0.401, whose code-style analyzers add rules the policy was not audited against: IDE0370 failed the first CI
  run on an unnecessary `!` in `RegistryParseTests` (removed as well; the 10.0.106 SDK lacks the rule).
- Locally a classic PAT with `read:packages` is stored for the source key `github` in the user NuGet config
  (README, Install).
- kbo is public and the package private (operator, 2026-10-08: accepted): a fork cannot restore the package.

## Converge

- Restore pulled `Sy11a.Analyzers` 0.5.0 from GitHub Packages (not in the local cache before);
  `dotnet build` 0 warnings, 0 errors; `dotnet test` 341/341.
- The policy is active: a throwaway file with a catch-all and a public type failed the build with CA1031, CA1515,
  CA1822, RCS1075 and IDE0055 as errors (removed again).
- CI on the PR: restore from GitHub Packages, build, 341/341.
- MiniMax wrote the build files, a Claude worker the test fix, Opus reviewed both.

✅ Converged 2026-10-08.
