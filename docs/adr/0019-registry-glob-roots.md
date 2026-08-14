# 0019. Registry glob roots

## Status

accepted

## Context

The reads-by-layer investigation showed that days of intense knowledge reading (OKF docs, ADRs in active repos) were invisible: the repos weren't registered, so every read carried `kbroot: null`. The owner wants current **and future** repos covered without editing the registry for each new one. Registering all of `~/Repository` as one root was rejected — vendored markdown (node_modules, reference dumps) would flood the dead-note inventory and code reads would drown the knowledge signal. The owner chose docs-subtree glob support over a static list, keeping the weekly audit's unregistered-sources finding as the reactive catch-all for knowledge appearing anywhere else.

## Decision

A registry source root may contain `*` as a whole path segment (e.g. `/home/u/Repository/*/docs`). At load time the entry expands to one concrete `KnowledgeSource` per matching directory:

- expanded id = `<entry-id>-<matched-segment>` (multiple `*` segments join with `-`), e.g. `repo-kb-observability`;
- matches are sorted ordinally for determinism; zero matches is valid (the glob may match later);
- partial-segment stars (`Repo*`) are rejected with a validation error;
- expanded ids participate in duplicate-id validation.

Everything downstream (Resolve, kbroot stamping, inventory, themes, audit) sees only the expanded concrete sources — no other component knows globs exist.

## Consequences

- New repos with a `docs/` folder are knowledge sources from their first event — no registry edit, because every load (capture, harvest, report) re-expands against the filesystem.
- Expansion cost is a few directory listings per load — negligible, but capture now touches the filesystem for expansion (accepted).
- Historical reads of newly matched roots surface immediately in registry-resolved gold (themes, audit) but not in the kbroot column of already-stamped events (bronze is immutable): the layer chart improves only going forward.
- Registering a docs subtree pulls its notes into the dead-note denominator — inventory jumped 597 → 1,071 and dead 34 → 148 on first expansion; intentional, that is the ritual's backlog.
- A repo deleted from disk silently drops out of the registry on next load (same behavior as a deleted literal root, which the inventory already skipped).
