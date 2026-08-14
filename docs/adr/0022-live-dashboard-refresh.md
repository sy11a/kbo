# 0022. Live dashboard refresh via foreground watch

## Status

accepted (bounds the "no real-time" line of ADR-0003 rather than crossing it)

## Context

The owner wanted the dashboard "live" instead of statically generated on the pulse cadence — driven by the repeated manual loop of `kbo report` + reopen + refresh. The data already arrives live (hooks/plugin write bronze on every tool call); only silver rebuild and gold render are batch. ADR-0003's NOT-list forbids "web server, resident daemon, OTel collector, or real-time anything" without an owner decision, and P2 requires renderers to contain zero computation (gold computed once). A live *server* querying state per request would cross both. The owner chose the lightest option that removes the manual friction without becoming a service (Option A of the offered menu; a localhost live-view server and full SSE streaming were the rejected heavier options, available later if watching sessions as they execute becomes a real need).

## Decision

Add `kbo watch [--interval <seconds>]` (default 30, minimum 5): a **foreground** command that, each tick, rebuilds silver from bronze and re-renders `kbo-dashboard.html` (and its gold twin) — the same static artifact, computed fresh per render — with a `<meta http-equiv="refresh" content="N">` tag so an open browser tab reloads itself. The loop uses `PeriodicTimer` and stops on cancellation (Ctrl-C); it is not a resident daemon, not registered with systemd, holds no port, and serves nothing. `DashboardRenderer.Render` gains an optional `autoReloadSeconds`; `report` and pulse still render without it (a weekly artifact should not busy-reload). Watch refreshes only the dashboard, not the markdown worklists — those are ritual artifacts, not live surfaces.

## Consequences

- The manual regenerate/reopen/F5 loop is gone: run `kbo watch`, leave the tab open, see current state within one interval. Still inside ADR-0003 (no server/daemon) and P2 (each render is a fresh compute-once, no incremental/live computation).
- Freshness is bounded by the interval and by rebuild cost (full delete-and-recreate, ~1–2s at current volume) — not sub-second, and not showing in-flight events until the next tick's rebuild. True per-event or in-flight-session liveness would require the rejected server/streaming options and an amendment to ADR-0003.
- The pulse dashboard is unaffected (no auto-reload); only the watch-rendered file self-refreshes.
