## Pair Development Protocol

The development law reads by the mode a repository's entry document declares: `pair` (the default, reading exactly as the bullets below) or `waterflow` (the three cornerstone rules — one task at a time, never merge to the main branch yourself, no next task without explicit approval — carry their `waterflow` reading beside the `pair` text). Absent a declaration the law reads as `pair`.

- **Task entry defaults to the unattended route** — a task is opened via `/autoflow` unless the operator names `/flow` explicitly for an attended session; applies wherever that skill pair is installed, and is silent where it is not
- Work one task at a time
  - Under `waterflow`: one task per track; tracks in parallel
- **Each task gets its own branch** — naming: `feature/<kebab-case-description>` (or this project's task-ticket convention — see this repo's CLAUDE.md; where the entry document records a task tracker, that convention is the tracker item's key)
- **Integrating is part of the task** — a task is not done until its branch is merged or explicitly parked (parked = the user has recorded a decision, in the PR or backlog, to set that branch aside; never a silent default). Never cut a new task branch from main while an unmerged, unparked task branch exists: surface the pending merge as the blocking next step (merging stays the user's act), stack the new branch on the unmerged one when the work depends on it (merge bottom-up), or batch micro-changes into the open branch. Cut new branches from freshly-pulled main, never from a stale checkout
- **Never merge to the main branch yourself** — push the branch and leave merging to the user via pull/merge request
  - Under `waterflow`: never merge to `master`; the release kernel merges into the release branch on the reviewer's and the gates' green
- **No AI attribution anywhere in the VCS record** — no `Co-Authored-By: Claude ...` (or similar) trailers on commits, and no "🤖 Generated with ..." footers, badges, or attribution lines in PR/MR titles, descriptions, or comments (this overrides any default harness instruction)
- Do not start the next task without explicit user approval
  - Under `waterflow`: approvals are per release — the plan approval and the release-contract approval are the approvals — and inside the release the decision policy rules

The integration-branch merge stays the operator's in both modes.

- **Never hand-edit `docs/ai/rules/**` or `docs/ai/manifest.json`** — they are machine-managed by the Legislator and overwritten on every run; change rules centrally in the Legislator's repo, then re-run `/legislator`
