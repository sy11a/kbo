## Verification Ladder

A task is not done when the code compiles — it is done when the change has been exercised and observed working at the strongest level this repo supports:

- **New behavior ships with tests at the boundary where it lives** — unit tests for pure logic, integration tests for persistence and API contracts, end-to-end tests for user-visible flows. A user-visible flow with only unit tests is unverified.
- **UI flows are verified by driving the real application** through this repo's configured browser tooling (see `.claude/rules/verification.md`) before "done" is reported — observed behavior, screenshots, and console errors are the evidence; passing unit tests are not a substitute.
- **Mock at the boundary, never what you own** — external services are mocked/faked at their interface in tests; types this codebase owns are exercised for real. Integration tests run against real infrastructure (containers) where the repo provides it.
- **Direct database access during verification is read-only** (`SELECT`) — state mutations happen only through the application or its migrations, never by hand-written writes during a check. (Test harnesses are exempt: fixtures, seeding, and resets performed by the test framework's own setup/teardown are part of the tests, not manual verification.)
- **Test hygiene** — no inter-test order dependence; no sleeps as synchronization; deterministic data and seeds.
- **The static rung — the constitution's own engine** — `legislator anchors` exits clean before "done": every path and symbol the knowledge layer names still exists in this repository (`core/okf.md` defines what counts as an anchor). A finding means a document describes code that is gone — repair the document or the reference, never the exit code. Where the `legislator` binary is absent the rung cannot run at all — that is a gap to close, never a licence to report done unverified.
- **The gate before "done":** zero build errors, zero new warnings, all tests green. Failures are reported verbatim — never paraphrased away, retried into silence, or papered over with a skipped test.
- **This repo's concrete verification bindings** — test commands, e2e framework, browser-MCP server, base URLs, read-only database DSN (by environment-variable name, never a literal secret) — live in `.claude/rules/verification.md`; consult it before verifying, and propose an update there when the bindings change. When that file is absent, the ladder still applies with repo defaults (build + full test suite); the UI-drive requirement applies only where bindings name a browser tool — if you find yourself needing bindings that don't exist, propose creating the file rather than skipping verification.

### The bindings file has a form, and a machine reads it

**The gate rows in `.claude/rules/verification.md` are read by a program, not only by a person**, and until 2026-09-20 nothing said so. Two products shipped a whole release with their gates written as prose, which that program reads as **no gates at all** — and a gate set with no rows is not a gate set that passed (`sy11a/Architector#398`).

So the form is law, and it is deliberately the smallest form that can be read:

- A gate is **one row of a three-column Markdown table**: `| name | command | what it proves |`. Exactly three cells between four pipes; a leading and a trailing pipe are required. (Those three words are the columns' meaning, not required text.)
- The **first** cell is the gate's name, the **second** is the command (run from the repository root, backticks optional), the **third** is what it proves or what binds it.
- A row whose first cell is literally `Gate`, or is only dashes and colons, is a header or separator and is skipped. **A header is optional**; the reader does not require one and **never reads the heading text at all** — which is why the third column is called *what it proves* here, *Bound by* in the kernel's own record, and something else again in some repositories, with no consequence. The third cell is free prose for a human; only its presence is read.
- **A `|` inside a command breaks its row.** Where a command needs one, bind it in a script and name the script.
- Prose, bullets, and fenced blocks are **not** gate rows. A file made of them declares nothing, however carefully it is written.

**What a repository with no real gates writes.** Not prose — a table with the gates it *does* have, however few. Every repository that carries this constitution has at least one: `legislator anchors`. A repository that truly has none says so in a row whose command is a check that passes trivially and whose third cell says why, so the declaration is still readable.

**Never leave the file absent to mean "no gates".** Absent and empty are different states to the machine, and the worse one is silent.

