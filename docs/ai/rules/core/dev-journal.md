## Developer Journal

`docs/journal/` holds one file per working day: `docs/journal/YYYY-MM-DD.md`.

A task branch adds the journal paragraph through its change fragment
(`docs/changes/<case>.md` `## journal` section) and never edits the day file
directly; `legislator render` assembles each day's file from the fragments that
name that day.

### What goes in an entry — and only this

The journal holds **what no other surface holds**. Three things qualify:

- **Dead ends** — approaches tried and abandoned, and why they were abandoned
- **Open questions** and context the next session needs
- **Decisions taken and the reasoning** (cross-link to an ADR if one was written)

**What does not go in it: the case narrative.** What was worked on, what changed,
and what it produced are the case's `summary.md`, and the changelog line points at
that summary. Repeating the story here is the same story told a third time, checked
by nothing and read by nobody — the cut this rule encodes.

Where a unit of work produced none of the three, write one line saying so. An
honest empty day keeps the day file current, which is what `journal-recency` reads,
and it is a truthful record: not every case leaves a dead end behind.

### When to write

- Write the paragraph in the `## journal` section of the case's change fragment —
  one paragraph per case, at task boundaries
- Not a running log of every action, and not a retelling of the case: a paragraph
  per completed unit of work, carrying only the three things above
- Create the day's file using `docs/journal/README.md`'s format if it doesn't
  exist yet