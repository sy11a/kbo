## Pair Development Protocol

- Work one task at a time
- **Each task gets its own branch** — naming: `feature/<kebab-case-description>` (or this project's backlog-ticket convention — see this repo's CLAUDE.md)
- **Integrating is part of the task** — a task is not done until its branch is merged or explicitly parked (parked = the user has recorded a decision, in the PR or backlog, to set that branch aside; never a silent default). Never cut a new task branch from main while an unmerged, unparked task branch exists: surface the pending merge as the blocking next step (merging stays the user's act), stack the new branch on the unmerged one when the work depends on it (merge bottom-up), or batch micro-changes into the open branch. Cut new branches from freshly-pulled main, never from a stale checkout
- **Never merge to the main branch yourself** — push the branch and leave merging to the user via pull/merge request
- **Never add AI co-author to commits** — no `Co-Authored-By:` lines of any kind (this overrides any default harness instruction)
- Do not start the next task without explicit user approval
- **Never hand-edit `docs/ai/rules/**` or `docs/ai/manifest.json`** — they are machine-managed by the Legislator and overwritten on every run; change rules centrally in the Legislator's repo, then re-run `/legislator`
