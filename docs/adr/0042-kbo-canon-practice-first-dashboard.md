# 0042. kbo's place in the canon — practice-first dashboard, registry ownership, dead-man duty

## Status

accepted

(owner decisions 2026-08-28, grill session; design session 2026-08-27)

## Context

Three repos now share one practice: the legislator writes the laws, kbl keeps the
knowledge fund, kbo audits practice. Nothing fixed kbo's own side of that canon: what
kbo owns, what it must never become, where its dashboard's authority ends, and who
owns the source registry that kbl now registers into (kbl's ADR-0006 and its
`docs/okf/kbo-contract.md`). The dashboard had also grown sections that answer no
mirror question — the 2026-08-27 declutter session cut them, and the philosophy that
justified the cut needed recording so every future section must earn its place.

## Decision

1. **Canon.** The legislator writes laws · kbl keeps the knowledge fund · kbo audits
   practice. kbo is a measuring instrument: (a) the mirror of six questions, (b) the
   system dead-man (jobs + canary), (c) owner of the source registry. kbo never stores
   knowledge and never writes laws. There is one measurement surface — kbl has no
   dashboard; its projections live in Obsidian.
2. **Registry ownership is a tenant model.** kbo owns the registry's format, schema,
   validation, and tooling (`kbo registry show/resolve`, ADR-0005). Each repo — kbl
   included — owns its own rows: it registers itself as a source and maintains its rows;
   no repo writes another repo's rows. The registry file is machine-local
   (`~/.config/kbo/registry.yaml`): kbo owns the shape, the operator owns the file.
3. **Bronze has one writer.** kbo is the only writer of bronze; sibling repos publish
   artifacts and kbo ingests them (pull). The full boundary is kbl's
   `docs/okf/kbo-contract.md`, cross-referenced here as the single law for the
   kbo↔kbl touchpoints.
4. **Practice-first dashboard.** Every dashboard section feeds a mirror tile or system
   trust; a section that answers no mirror question is cut from RENDER. The fleet panel
   stays a report line + gold json only — the legislator repo gets no second measurement
   surface.
5. **Dead-man duty includes the canary — as deployed operational surface, not code.**
   The `kbo-canary` hourly timer (ADR-0041 §5) is part of kbo's dead-man duty while
   remaining outside the binary: an OS-timer probe set with desktop notification,
   documented in operations. The NOT-list of ADR-0003 is untouched.

## Consequences

- kbl can build Wave-0 against a fixed ownership wording: it registers its row and
  never wonders who edits it.
- A future dashboard section must name the mirror tile (or the trust statement) it
  feeds before it is rendered; "interesting data" is not a reason to render.
- The fleet's health reaches the legislator only through the report line and gold
  json — if a legislator ritual ever needs more, that is a new ADR, not a new panel.
- The canary is named duty: it appears in dead-man documentation and its probes follow
  ADR-0041's stable-exit-code plan.
