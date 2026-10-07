using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Adapters.ClaudeCode;
using Kbo.Bronze;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Tests;

public sealed class ClaudeCodeSessionStartTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _home;
    private readonly string _repoRoot;
    private readonly KnowledgeRegistry _registry;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly TimeProvider _clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-08-11T15:00:00Z", CultureInfo.InvariantCulture));

    public ClaudeCodeSessionStartTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-session-tests").FullName;
        _home = Path.Combine(_workspace, "home");
        _repoRoot = Path.Combine(_workspace, "repo");

        _ = Directory.CreateDirectory(Path.Combine(_home, ".claude"));
        File.WriteAllText(Path.Combine(_home, ".claude", "CLAUDE.md"), "global instructions\n");

        _ = Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        File.WriteAllText(Path.Combine(_repoRoot, ".git", "HEAD"), "ref: refs/heads/feature/AC-9-hook\n");
        File.WriteAllText(Path.Combine(_repoRoot, "CLAUDE.md"), "hello\n");
        _ = Directory.CreateDirectory(Path.Combine(_repoRoot, ".claude", "rules"));
        File.WriteAllText(Path.Combine(_repoRoot, ".claude", "rules", "skills.md"), "rules\n");

        string memoryDirectory = Path.Combine(_home, ".claude", "projects", _repoRoot.Replace('/', '-'), "memory");
        _ = Directory.CreateDirectory(memoryDirectory);
        File.WriteAllText(Path.Combine(memoryDirectory, "MEMORY.md"), "memory index\n");

        _registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            taskPattern: 'AC-\d+'
            sources:
              - id: repo-kb
                layer: local
                root: {_repoRoot}
            """);
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private List<JsonObject> MapSessionStart()
    {
        JsonObject payload = new()
        {
            ["session_id"] = "sess-0002",
            ["cwd"] = _repoRoot,
            ["hook_event_name"] = "SessionStart",
            ["source"] = "startup",
        };
        return ClaudeCodeAdapter.MapSessionStart(payload, _registry, _clock, CryptographicUlidEntropy.Instance, _home);
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
    public void WithoutTaskPattern_TaskIsNull_ButBranchIsKept()
    {
        KnowledgeRegistry noPatternRegistry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: repo-kb
                layer: local
                root: {_repoRoot}
            """);
        JsonObject payload = new()
        {
            ["session_id"] = "sess-0003",
            ["cwd"] = _repoRoot,
            ["hook_event_name"] = "SessionStart",
            ["source"] = "startup",
        };

        List<JsonObject> events = ClaudeCodeAdapter.MapSessionStart(payload, noPatternRegistry, _clock, CryptographicUlidEntropy.Instance, _home);

        JsonObject started = events[0];
        EventValidationResult result = new EventValidator().Validate(started.ToJsonString());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Null(started["task"]);
        Assert.Equal("feature/AC-9-hook", (string?)started["data"]!["branch"]);
    }

    [Fact]
    public void EmitsContextLoaded_ForEachExistingImplicitFile()
    {
        List<JsonObject> events = MapSessionStart();
        List<JsonObject> loaded = [.. events.Where(e => (string?)e["type"] is "context.loaded")];

        EventValidator validator = new();
        foreach (JsonObject contextEvent in loaded)
        {
            EventValidationResult result = validator.Validate(contextEvent.ToJsonString());
            Assert.True(result.IsValid, string.Join("; ", result.Errors));
        }

        List<string?> paths = [.. loaded.Select(e => (string?)e["subject"])];
        Assert.Contains(Path.Combine(_home, ".claude", "CLAUDE.md"), paths, StringComparer.Ordinal);
        Assert.Contains(Path.Combine(_repoRoot, "CLAUDE.md"), paths, StringComparer.Ordinal);
        Assert.Contains(Path.Combine(_repoRoot, ".claude", "rules", "skills.md"), paths, StringComparer.Ordinal);
        Assert.Contains(paths, p => p!.EndsWith("MEMORY.md", StringComparison.Ordinal));

        JsonObject projectInstructions = loaded.Single(e => (string?)e["subject"] == Path.Combine(_repoRoot, "CLAUDE.md"));
        Assert.Equal("repo-kb", (string?)projectInstructions["kbroot"]);
        Assert.Equal("5891b5b522d5df08", (string?)projectInstructions["data"]!["contenthash"]);
        Assert.Equal("project-instructions", (string?)projectInstructions["data"]!["raw"]!["kind"]);

        JsonObject globalInstructions = loaded.Single(e => (string?)e["subject"] == Path.Combine(_home, ".claude", "CLAUDE.md"));
        Assert.Null(globalInstructions["kbroot"]);
        Assert.Null(globalInstructions["data"]!["contenthash"]);
        Assert.Equal("global-instructions", (string?)globalInstructions["data"]!["raw"]!["kind"]);
    }
}
