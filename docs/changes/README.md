# Change Fragments

One fragment per case, named for its case key: `<case>.md` — `BL-193.md`, `L-3.md`.
**The name is what makes a file a fragment**, for `sdd-lint` and for `render`
alike: a file whose name is not a case key — this README included — is the
home's own furniture, and one whose name *is* a case key must declare that same
case in its front matter.

Each fragment carries YAML front matter — `case`, `issue`, `kind`, `date`, all
four required, `kind` one of `Added`, `Changed`, `Fixed`, `Removed` — and:

- `## changelog` (**required**) — **exactly one** `-` bullet, the line this case
  adds to `[Unreleased]`, pointing at the case's own record. A second bullet is
  the signal that the case was two cases.
- `## journal` (**required**) — one paragraph carrying only what no other
  surface holds: dead ends and why they were abandoned, open questions for the
  next session, and decisions with their reasoning. Not the case narrative —
  that is the summary's. Where the unit produced none of the three, say so in
  one line rather than omitting the section.
- `## okf-log` (**optional, normally absent**) — owed only where a concept
  document's meaning changed and no case summary records it. Inside a case the
  summary is that record, so the section is omitted.

A task branch adds one fragment and never edits `CHANGELOG.md`,
`docs/okf/log.md` or `docs/journal/` directly. Those three views are written by
`legislator render` on the default branch. See
`docs/ai/rules/core/changelog.md` for the full discipline — this file explains
the home; that file is the law.
