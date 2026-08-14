# Event taxonomy registry

Rule (`04 - Event Schema`): a type or field exists only if a report question demands it. One row per type; **an empty "justified by" cell is a stop sign.**

| Type | Domain | Justified by (report question) | Key data fields | Status |
|---|---|---|---|---|
| `knowledge.read` | Discoverability | dead/hot notes, session profiles | path, `contenthash` | v1 |
| `knowledge.searched` | Discoverability | failed-KB-search rate (searched-but-not-found = discoverability failure) | pattern, root searched, hit count (hook best-effort, harvest authoritative — G2-6) | v1 |
| `knowledge.written` | Corpus | capture ROI ("notes written during captures ever read again?") | path, `contenthash`/`size` of the on-disk file (write→read correlation; written content itself is never embedded — ADR-0030) | v1 |
| `context.loaded` | Capture | which implicit context (CLAUDE.md/AGENTS.md/memory) was in play, at which version | path, `contenthash` | v1 |
| `session.started` | Capture | session inventory; denominators | agent, model, repo, raw git branch; usage totals incl. `cache_read` vs fresh input tokens | v1 |
| `job.completed` / `job.failed` | Capture (self) | dead-man health panel | job name, duration, error | v1 |
| `skill.invoked` | Discoverability | dead/overused skills | skill name, args | reserved in v1; backfillable from transcript archive |
| `web.searched` / `web.fetched` | Coverage | recurring-web-topic → note candidates | full query text / URL (owner decision: full fidelity, private storage) | reserved in v1; backfillable |

Misses are never captured — they are derived at analysis time. Capture stays dumb and total (P1, G2-1).
