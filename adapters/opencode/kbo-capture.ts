// opencode -> kbo live capture (adapter contract #1, ADR-0014).
// Install: copy (or symlink) into ~/.config/opencode/plugins/.
// Best-effort by design: hands payloads to `kbo capture opencode` in a
// detached process and never throws — the session is never blocked or
// failed by capture (G2-6: hooks best-effort, harvest authoritative).
import { spawn } from "node:child_process";

const KBO_BIN = process.env.KBO_BIN ?? `${process.env.HOME}/.local/bin/kbo`;
const CAPTURED_TOOLS = new Set(["read", "grep", "glob", "write", "edit"]);

function send(payload: Record<string, unknown>) {
  try {
    const child = spawn(KBO_BIN, ["capture", "opencode"], {
      detached: true,
      stdio: ["pipe", "ignore", "ignore"],
    });
    child.on("error", () => {});
    child.stdin.on("error", () => {});
    child.stdin.write(JSON.stringify(payload));
    child.stdin.end();
    child.unref();
  } catch {
    // capture must never break the session
  }
}

export const KboCapture = async ({ directory, worktree }: { directory: string; worktree?: string }) => {
  let sessionAnnounced = false;
  return {
    event: async ({ event }: { event: { type?: string; properties?: Record<string, unknown> } }) => {
      if (sessionAnnounced || (event?.type !== "session.created" && event?.type !== "session.idle")) {
        return;
      }
      sessionAnnounced = true;
      const properties = event.properties ?? {};
      send({
        hook_event_name: "session.start",
        session_id: (properties["sessionID"] ?? (properties["info"] as Record<string, unknown>)?.["id"]) ?? null,
        directory: worktree ?? directory,
      });
    },
    "tool.execute.after": async (
      input: { tool: string; sessionID: string; callID: string; args: unknown },
    ) => {
      if (!CAPTURED_TOOLS.has(input.tool)) {
        return;
      }
      send({
        hook_event_name: "tool.execute.after",
        session_id: input.sessionID,
        directory: worktree ?? directory,
        tool: input.tool,
        call_id: input.callID,
        args: input.args ?? {},
      });
    },
  };
};
