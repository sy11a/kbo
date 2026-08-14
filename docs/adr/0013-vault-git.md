# 0013. Vault git: per-pulse auto-commit job, inline identity, _generated included

## Status

Accepted (executor decisions within `07 - Feature Specs` §Vault git and G2-12; plan step 2.6, owner-approved in the plan)

## Context

The spec fixes: local `git init` in the vault, scheduled auto-commit with a fixed message pattern, no remote. Left open: cadence within "daily or per-pulse", commit identity, and whether generated content is versioned.

## Decision

1. **Per-pulse** (G2-12: "vault auto-commit rides every pulse"): `VaultGitJob` runs every pulse, ordered after archive and **before backup** so restic snapshots capture the committed state.
2. **Fixed message pattern**: `kbo auto-commit <UTC ISO timestamp>`; commit identity passed inline (`-c user.name=kbo -c user.email=kbo@localhost`) so the job never depends on the machine's git config.
3. **`_generated/` is versioned too**: gold twins and reports gain history for free — the drift metric ("notes fixed after appearing in a report") can diff a report against the vault state that followed it.
4. **"No changes" is a normal completion**, not a skip — the job event still lands, so the dead-man tile watches the job, not the vault's churn.
5. Init is idempotent (`git init` only when `.git` is absent); a missing vault is a job failure, not a silent no-op.

## Consequences

Point-in-time note content is retrievable by date (`git -C ~/Knowledge log --until=<date>` + `git show <rev>:<note>`), which is the step's acceptance and the future judge's substrate. Vault history lives only locally + in restic (by design — no remote; durability is backup's job, G2-7/G2-8). Bulk vault operations (ritual archive/merge sweeps) are now safely revertible.
