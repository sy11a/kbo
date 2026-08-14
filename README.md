# kb-observability (`kbo`)

**Practice Observability for AI-assisted development** — see whether your
knowledge base is actually used by AI coding agents, and make it better.

As you work with AI agents (Claude Code, opencode), `kbo` captures what they
**read, search, and write**, and which **skills** they invoke. It turns that
into a static dashboard and per-day digests that answer questions you otherwise
can only guess at:

- Is my knowledge base actually in the loop, or do agents solve everything from scratch?
- Which notes are load-bearing, and which are dead weight?
- What do agents search for and fail to find? (i.e. what should I write down?)
- Does the knowledge agents *create* get reused?
- Is the practice getting better week over week?

Everything runs **locally**. No server, no daemon, no data leaves your machine.

## How it works — a bronze / silver / gold pipeline

| Layer | What it is |
|-------|------------|
| **Bronze** | Append-only NDJSON event log — immutable and sufficient. Every derived layer can be rebuilt from it alone. |
| **Silver** | A disposable DuckDB database derived from bronze by `kbo rebuild`. Delete it any time; it rebuilds. |
| **Gold** | Report-ready facts computed **exactly once**, rendered into a static HTML dashboard and Markdown pages. Renderers contain zero computation. |

Agents are observed through **adapters** (a Claude Code hook, an opencode
plugin) — the only agent-specific code in the system. A per-machine **registry**
maps file paths to knowledge sources, so a read of `~/Knowledge/note.md` is
tagged as knowledge while a read of `src/Program.cs` is not.

## Install

```bash
dotnet build
dotnet test
dotnet publish src/Kbo -c Release -p:PublishSingleFile=true --self-contained false -o ~/.local/bin
```

This puts a `kbo` binary on your `PATH`.

## Quick start

1. **Create a registry** at `~/.config/kbo/registry.yaml` describing where your
   knowledge lives. See [`registry/example.yaml`](registry/example.yaml). A root
   may use a `*` segment (e.g. `~/Repository/*/docs`) to auto-include every repo.

   ```bash
   kbo registry show                 # list resolved sources
   kbo registry resolve <some/path>  # which source does a path belong to?
   ```

2. **Install a capture adapter** so agents emit events (see [`adapters/`](adapters/)):
   - Claude Code: register the hook from `adapters/claude-code/`.
   - opencode: symlink the plugin from `adapters/opencode/`.

3. **Schedule the pipeline**:

   ```bash
   kbo init      # installs a systemd user timer (hourly "pulse") + a login-time health check
   ```

   The pulse harvests transcripts, rebuilds silver, archives, backs up, and
   (weekly) regenerates the report and audit. Each job decides its own due-ness
   from bronze, so an off-at-midnight machine catches up at power-on.

4. **See the results**:

   ```bash
   kbo report                        # regenerate the dashboard + day pages now
   kbo watch                         # foreground live-refresh (self-reloading page)
   xdg-open ~/Knowledge/_generated/kbo-dashboard.html
   ```

## Commands

```
kbo registry   inspect / resolve the knowledge registry
kbo capture    ingest one live agent event (called by the adapters)
kbo harvest    mine agent transcripts into bronze (backfill / recovery)
kbo rebuild    rebuild silver from bronze
kbo report     compute gold and render the dashboard + day pages
kbo audit      report capture gaps and unregistered knowledge
kbo pulse      run all scheduled jobs whose cadence is due
kbo init       install the systemd user timer + login health check
kbo doctor     health check (timer armed? any job silent > 3 days?)
kbo watch      live-refresh the dashboard on an interval
```

## What the dashboard shows

Health tiles (dead-man detection per job and per agent), sessions by repository,
recent per-session activity, reads by layer and by content type, most-read and
never-read themes, most-reused notes, the write→read loop, KB-touch and
failed-search rates with target zones, top skills, top zero-hit searches,
token usage, and week-over-week deltas. Per-day digests land as Markdown pages
alongside it.

## Design

- **Principles**: bronze is immutable and sufficient; gold is computed once;
  schema evolution is additive-only (golden corpus in CI); nothing
  agent/path-specific is hardcoded outside adapters and the registry.
- **The NOT-list** (`docs/adr/0003-*`): no custom storage engine, web server,
  resident daemon, OTel collector, or real-time anything — deliberately a batch,
  reproducible tool.
- Architecture decisions live in [`docs/adr/`](docs/adr/); concept documentation
  in [`docs/okf/`](docs/okf/); a domain glossary in `docs/okf/glossary.md`.

## Repository layout

```
adapters/   agent-side capture assets (Claude Code hook, opencode plugin)
charts/     owner-editable Vega-Lite chart specs (embedded at build)
schemas/    event JSON Schemas + golden corpus (embedded at build)
registry/   sanitized example registry
src/        the Kbo project (the kbo CLI)
tests/      xUnit test suite
docs/       ADRs, OKF knowledge bundle, operations notes
```

## License

[MIT](LICENSE) © sy11a
