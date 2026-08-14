using Kbo.Jobs;

namespace Kbo.Adapters.Opencode;

/// <summary>
/// Retention manifest only (adapter contract #3) — the full opencode adapter
/// (plugin + implicit loads) is plan step 2.3. The session store is a SQLite
/// database, not per-session files; auth.json is deliberately excluded (secrets).
/// </summary>
public static class OpencodeRetention
{
    public const string AgentName = "opencode";

    public static RetentionManifest Manifest(string homeDirectory)
    {
        string dataDirectory = Path.Combine(homeDirectory, ".local", "share", "opencode");
        string databasePath = Path.Combine(dataDirectory, "opencode.db");
        return new RetentionManifest(
            AgentName,
            new ArchiveEntry[]
            {
                new SqliteEntry(databasePath, "opencode", "opencode-latest.db", "opencode-"),
                new FileTreeEntry(Path.Combine(dataDirectory, "tool-output"), "*", "opencode/tool-output"),
                new FileTreeEntry(Path.Combine(dataDirectory, "snapshot"), "*", "opencode/snapshot"),
            },
            SessionDatabase: new SqliteSessionSource(databasePath, "SELECT id, time_updated FROM session"));
    }
}
