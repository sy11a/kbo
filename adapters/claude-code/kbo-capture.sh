#!/usr/bin/env bash
# Claude Code -> kbo live capture (adapter contract #1, ADR-0006).
# Best-effort by design: reads the hook payload, hands it to `kbo capture`
# in the background, and always exits 0 immediately — the session is never
# blocked or failed by capture (G2-6: hooks best-effort, harvest authoritative).

payload=$(cat)

kbo_bin="${KBO_BIN:-$HOME/.local/bin/kbo}"
log_dir="${XDG_STATE_HOME:-$HOME/.local/state}/kbo"
mkdir -p "$log_dir"

if [ ! -x "$kbo_bin" ]; then
  echo "$(date -u +%FT%TZ) kbo binary not found at $kbo_bin" >>"$log_dir/hook.log"
  exit 0
fi

printf '%s' "$payload" | setsid "$kbo_bin" capture claude-code >>"$log_dir/hook.log" 2>&1 &

exit 0
