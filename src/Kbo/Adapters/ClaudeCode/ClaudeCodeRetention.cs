using Kbo.Jobs;

namespace Kbo.Adapters.ClaudeCode;

public static class ClaudeCodeRetention
{
    public static RetentionManifest Manifest(string homeDirectory)
    {
        string claudeDirectory = Path.Combine(homeDirectory, ".claude");
        FileTreeEntry sessionFiles = new(Path.Combine(claudeDirectory, "projects"), "*.jsonl", "claude-code/projects");
        return new RetentionManifest(
            ClaudeCodeAdapter.AgentName,
            new ArchiveEntry[]
            {
                sessionFiles,
                new SingleFileEntry(Path.Combine(claudeDirectory, "history.jsonl"), "claude-code/history.jsonl"),
            },
            SessionFiles: sessionFiles);
    }
}
