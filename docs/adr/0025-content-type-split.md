# 0025. Content-type split (knowledge vs code)

## Status

accepted

## Context

A data probe showed that only ~59% of "knowledge reads" on registered roots are actual notes (`.md`); the other ~41% is source code and config (`.cs` 23%, `.tpl`, `.sh`, …), swept in because some projects are registered as whole repos. Every read-based metric — KB-touch, reuse, themes, reads-by-layer — counts code reads as knowledge, inflating them. For a tool about the *knowledge base*, that conflation is the biggest accuracy gap.

## Decision

Add `ContentKind.Of(path)` — a pure, extension-based classifier into `knowledge` (`.md`, `.markdown`, `.txt`, …), `code` (`.cs`, `.ts`, `.py`, …), `config` (`.json`, `.yaml`, `.tpl`, …), `other`. Surface it as a **"Reads by content type"** ranked breakdown on the dashboard (over the 60-day window, registered reads only), with a description stating explicitly that the other metrics still count all registered reads including code — so the split shows what fraction is genuinely knowledge.

Derived at gold-compute time from the `subject` path; no schema or capture change, works retroactively on all existing data. Existing metrics are **not** redefined in this step (KB-touch etc. keep counting all registered reads) — the split is added as a visible dimension; a later change may offer notes-only variants (e.g. the reuse lens scopes to notes).

## Consequences

- The dashboard now shows the code/knowledge composition honestly (knowledge 59% / code 29% / config 6% / other 4% on real data), so a high KB-touch driven by code reads is legible rather than hidden.
- `ContentKind` is reusable by downstream lenses (reuse/ROI, write→read loop) to scope to actual notes.
- Classification is extension-based and best-effort; extension-less files fall to `other`. The extension sets are code, editable if a stack is missing.
