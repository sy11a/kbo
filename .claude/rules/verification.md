# Verification Bindings (this repo)

The gate commands this repository declares, all run from the repository root,
each exiting 0 before "done" is reported. The law that creates this file is
`docs/ai/rules/core/verification.md`; this is the declaration.

**A machine reads the rows below, not only a person.** A gate is one row of this
three-column table. Prose and bullets declare nothing — a file written as prose
is read as **no gates at all**, and a gate set with no rows is not a gate set
that passed. Keep every gate in the table; put the reasoning around it.

| Gate | Command | What it proves |
|------|---------|----------------|
| anchors | `legislator anchors` | every path or symbol an anchored OKF document backticks still resolves |
| sdd-lint | `legislator sdd-lint` | case, ADR, journal, changelog and OKF shape law |
| build | `dotnet build` | the solution compiles |
| test | `dotnet test` | the test suite is green |

Add a row per real gate as the repository gains one — build, test suite, an e2e
run, a corpus check, a self-test. A row whose command contains a `|` breaks: bind
such a command in a script and name the script here.

**A repository with no build and no tests still declares.** The row above is
runnable wherever the `legislator` binary is on `PATH`, which is where this
constitution is worked — and `core/verification.md` is explicit that **where the
binary is absent the rung cannot run at all**, which is a gap to close, not a
reason to write prose here instead. If that is your situation, keep the row and
say so in its third cell; a gate that cannot run is a different thing from a gate
nobody declared, and only one of them is visible.

Never leave this file absent to mean "no gates": absent and empty are different
states to the machine, and the worse one is silent.

The rows above were written at the edition-27 delivery. The build and test rows are transcribed from this repository's own `## Build & Test` section; nothing was invented.
