## Architecture Decision Records (ADR)

Record a decision in `docs/adr/` whenever:

- A decision-gate stop (see `decision-gate.md`) is resolved by the user
- A new architecture invariant is introduced (e.g. a layering rule, a required abstraction)
- An accepted antipattern or known tradeoff is deliberately kept instead of fixed

### Format

- File: `docs/adr/NNNN-kebab-case-title.md`, numbered sequentially starting at `0001`
- Use the template at `docs/adr/template.md`: Status, Context, Decision, Consequences
- Status is one of: `proposed`, `accepted`, `deprecated`, `superseded by NNNN`
- Never renumber or delete a past ADR — supersede it with a new one and update its status

### When to write it

- Write the ADR as part of the same task that made the decision, not as a follow-up
- Link the ADR from the relevant OKF document(s) it affects
