using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Adapters.ClaudeCode;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Tests;

public class ClaudeCodeSessionStartTests : IDisposable
{
    private readonly string workspace;
    private readonly string home;
    private readonly string repoRoot;
    private readonly KnowledgeRegistry registry;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly TimeProvider Clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-08-11T15:00:00Z", CultureInfo.InvariantCulture));

    public ClaudeCodeSessionStartTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-session-tests").FullName;
        home = Path.Combine(workspace, "home");
        repoRoot = Path.Combine(workspace, "repo");

        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        File.WriteAllText(Path.Combine(home, ".claude", "CLAUDE.md"), "global instructions\n");

        Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
        File.WriteAllText(Path.Combine(repoRoot, ".git", "HEAD"), "ref: refs/heads/feature/AC-9-hook\n");
        File.WriteAllText(Path.Combine(repoRoot, "CLAUDE.md"), "hello\n");
        Directory.CreateDirectory(Path.Combine(repoRoot, ".claude", "rules"));
        File.WriteAllText(Path.Combine(repoRoot, ".claude", "rules", "skills.md"), "rules\n");

        string memoryDirectory = Path.Combine(home, ".claude", "projects", repoRoot.Replace('/', '-'), "memory");
        Directory.CreateDirectory(memoryDirectory);
        File.WriteAllText(Path.Combine(memoryDirectory, "MEMORY.md"), "memory index\n");

        registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: repo-kb
                layer: local
                root: {repoRoot}
            """);
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private List<JsonObject> MapSessionStart()
    {
        JsonObject payload = new()
        {
            ["session_id"] = "sess-0002",
            ["cwd"] = repoRoot,
            ["hook_event_name"] = "SessionStart",
            ["source"] = "startup",
        };
        return ClaudeCodeAdapter.MapSessionStart(payload, registry, Clock, new Random(42), home);
    }

    [Fact]
    public void EmitsSessionStartedFirst_WithBranchAndTask()
    {
        List<JsonObject> events = MapSessionStart();

        JsonObject started = events[0];
        EventValidationResult result = new EventValidator().Validate(started.ToJsonString());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal("session.started", (string?)started["type"]);
        Assert.Equal("sess-0002", (string?)started["subject"]);
        Assert.Equal("feature/AC-9-hook", (string?)started["data"]!["branch"]);
        Assert.Equal("AC-9", (string?)started["task"]);
        Assert.Null(started["data"]!["usage"]);
    }

    [Fact]
    public void EmitsContextLoaded_ForEachExistingImplicitFile()
    {
        List<JsonObject> events = MapSessionStart();
        List<JsonObject> loaded = events.Where(e => (string?)e["type"] == "context.loaded").ToList();

        EventValidator validator = new();
        foreach (JsonObject contextEvent in loaded)
        {
            EventValidationResult result = validator.Validate(contextEvent.ToJsonString());
            Assert.True(result.IsValid, string.Join("; ", result.Errors));
        }

        List<string?> paths = loaded.Select(e => (string?)e["subject"]).ToList();
        Assert.Contains(Path.Combine(home, ".claude", "CLAUDE.md"), paths);
        Assert.Contains(Path.Combine(repoRoot, "CLAUDE.md"), paths);
        Assert.Contains(Path.Combine(repoRoot, ".claude", "rules", "skills.md"), paths);
        Assert.Contains(paths, p => p!.EndsWith("MEMORY.md"));

        JsonObject projectInstructions = loaded.Single(e => (string?)e["subject"] == Path.Combine(repoRoot, "CLAUDE.md"));
        Assert.Equal("repo-kb", (string?)projectInstructions["kbroot"]);
        Assert.Equal("5891b5b522d5df08", (string?)projectInstructions["data"]!["contenthash"]);
        Assert.Equal("project-instructions", (string?)projectInstructions["data"]!["raw"]!["kind"]);

        JsonObject globalInstructions = loaded.Single(e => (string?)e["subject"] == Path.Combine(home, ".claude", "CLAUDE.md"));
        Assert.Null(globalInstructions["kbroot"]);
        Assert.Null(globalInstructions["data"]!["contenthash"]);
        Assert.Equal("global-instructions", (string?)globalInstructions["data"]!["raw"]!["kind"]);
    }
}
