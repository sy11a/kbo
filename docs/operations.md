# Operating kbo — owner cheat sheet

Everything runs itself; this page is for the rare moments you want to look under the hood.

## Day-to-day (usually nothing)

| I want to… | Command |
|---|---|
| Check everything is healthy | `kbo doctor` (runs automatically at every login with a desktop notification) |
| See when the next pulse fires | `systemctl --user list-timers kbo-pulse.timer` |
| Watch what the last pulse did | `journalctl --user -u kbo-pulse.service -n 30` |
| Force a pulse right now | `kbo pulse` (safe any time — jobs already done today just skip) |
| Regenerate report + dashboard now | `kbo rebuild && kbo report` |
| Watch the dashboard live | `kbo watch` (foreground; rebuilds + re-renders every 30s, tab self-reloads; `--interval <s>`, min 5; Ctrl-C to stop) |
| Open the worklist / dashboard | `~/Knowledge/_generated/kbo-report.md` (Obsidian) · `kbo-dashboard.html` (browser) |
| Check capture completeness | `kbo audit` (also weekly via pulse) |
| Resolve a path to its kbroot | `kbo registry resolve <path>` · list roots: `kbo registry show` |

## How the pieces sit

- **Timer**: `kbo-pulse.timer` ticks hourly (`Persistent=true`); `kbo pulse` itself skips jobs already completed today (read from bronze) — off-at-midnight machines catch up at power-on; failed jobs retry hourly (ADR-0015).
- **Login check**: `kbo-doctor.service` runs `kbo doctor --notify` at every login — desktop notification, critical if the timer is dead or any job has been silent > 3 days (ADR-0016).
- **Jobs** (daily): harvest, harvest-opencode, rebuild, archive, vault-git, bronze-git, backup; (weekly): report, audit. Every run lands as a `job.*` event in bronze — the dashboard's dead-man tiles read those.
- **Data**: bronze `~/Repository/kb-events` (append-only, the only truth) · silver `~/.local/share/kbo/silver.duckdb` (disposable — delete + `kbo rebuild` any time) · gold + reports in `~/Knowledge/_generated/` (overwritten every report run).
- **Config**: registry `~/.config/kbo/registry.yaml` (add knowledge roots here; `kbo registry show` to verify) · env overrides `KBO_REGISTRY`, `KBO_EVENTS_REPO`, `KBO_SILVER`, `KB_ARCHIVE_ROOT`, `KB_RESTIC_REPO`.

## After changing kbo's code

```
dotnet publish src/Kbo -c Release -p:PublishSingleFile=true --self-contained false -o ~/.local/bin
```
(the hook, plugin, and services all call `~/.local/bin/kbo`). If unit files changed: `kbo init` re-registers them idempotently.

## Recovery moves

| Situation | Move |
|---|---|
| Timer/service broken or after `kbo init` changes | `kbo init` (idempotent; re-arms everything) |
| Capture gap suspected | `kbo audit` → follow its `kbo harvest <agent>` recovery line |
| Silver looks wrong | delete `~/.local/share/kbo/silver.duckdb`, `kbo rebuild` (P3: always reproducible) |
| Restore a retired skill pack | `mv ~/.claude/skills-retired/<name> ~/.claude/skills/` |
| Bring back Phase 0 timers (fallback) | `systemctl --user enable --now kb-archive.timer kb-backup.timer` |
| Point-in-time note content | `git -C ~/Knowledge log --until=<date>` then `git show <rev>:<note path>` |
| Restic snapshots | `restic --repo ~/Backups/kb-restic --password-file ~/.config/kb-observability/restic-password snapshots` |
