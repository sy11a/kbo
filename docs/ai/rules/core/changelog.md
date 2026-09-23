## Changelog

The root `CHANGELOG.md` keeps the [Keep a Changelog](https://keepachangelog.com/)
structure. A task branch adds one change fragment and never edits `CHANGELOG.md`
directly; the three rendered views — `CHANGELOG.md`'s `[Unreleased]` section,
`docs/okf/log.md` and `docs/journal/YYYY-MM-DD.md` — are written by
`legislator render` on the default branch, never on a task branch.

### Change fragments

One fragment per case: `docs/changes/<case>.md`, YAML front matter, three sections.

**Front matter (required, all four keys):**
- `case` — the case key (`BL-NNN` or `L-N`)
- `issue` — the tracker issue number or URL
- `kind` — one of `Added`, `Changed`, `Fixed`, `Removed`
- `date` — `YYYY-MM-DD`

**Sections:**
- `## changelog` (required) — **exactly one** `-` bullet for the `[Unreleased]`
  section: what the case changed, in the effect it had, linking the case's own
  record (`docs/cases/<case>/summary.md`). The summary is the telling; this line
  is the pointer to it, and a second bullet is the signal that the case was two
  cases.
- `## okf-log` (optional, normally absent) — one paragraph for `docs/okf/log.md`,
  owed **only** where a concept document's meaning changed and no case summary
  records it. Inside a case the summary is that record, so the section is omitted;
  a concept change made outside any case is written straight into `docs/okf/log.md`
  instead, because nothing else holds it.
- `## journal` (required) — one paragraph for the day's
  `docs/journal/YYYY-MM-DD.md`, holding **only what no other surface holds**: dead
  ends (what was tried, abandoned, and why), open questions for the next session,
  and decisions with their reasoning. The case narrative belongs in the summary and
  must not be repeated here. Where the unit produced none of the three, say so in
  one line rather than omitting the section — the day file stays current, which is
  what `journal-recency` reads.

**Example:**
```markdown
---
case: BL-193
issue: 327
kind: Added
date: 2026-09-16
---

## changelog

- Change fragments replace hand-edited views: a case adds `docs/changes/<case>.md` and `legislator render` writes `CHANGELOG.md`, `docs/okf/log.md` and `docs/journal/` from it ([BL-193](docs/cases/BL-193-change-fragments/summary.md)).

## journal

- Dead end: rendering the journal per fragment-day first, which made a re-render rewrite days that were already history. Reversed to insert-once-by-case-key. Open for the next session: whether a release cut should delete fragments or archive them.
```

The example carries no `## okf-log` section, which is the ordinary case: the
concept change it describes is recorded by `docs/cases/BL-193-change-fragments/summary.md`.

### Render discipline

- `legislator render` is idempotent by case key: a fragment whose case key already
  appears in a view is not inserted twice, and nothing else in the view is
  touched. A malformed fragment is refused naming the defect and the file.
- Running `legislator render` on a task branch is refused — render runs only
  on the default branch. `sdd-lint` checks fragments (front matter, kind in the
  closed set, case matching the branch, **exactly one `## changelog` bullet**, and
  a `## journal` section that is present) where it used to check the
  `[Unreleased]` section's shape by hand. The one-bullet rule is the first gate any
  of these three surfaces has ever had; the `## journal` section's *content* is not
  machine-checkable and is not pretended to be.
- At a release cut the `[Unreleased]` section folds under a `## [vN]` heading
  and the fragments are deleted.

### When to update

- Add a `docs/changes/<case>.md` fragment as part of every task completion
  checklist — the same commit that finishes the task adds its fragment
- Move `[Unreleased]` entries under a dated version heading only when the
  user cuts a release
- Write entries for the change's user-visible or API-visible effect, not
  implementation detail