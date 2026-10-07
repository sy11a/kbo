using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Adapters.Opencode;
using Kbo.Bronze;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Tests;

public sealed class OpencodeAdapterTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _vaultRoot;
    private readonly string _repoRoot;
    private readonly KnowledgeRegistry _registry;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly TimeProvider _clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-08-12T23:30:00Z", CultureInfo.InvariantCulture));

    public OpencodeAdapterTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-oc-adapter-tests").FullName;
        _vaultRoot = Path.Combine(_workspace, "Knowledge");
        _repoRoot = Path.Combine(_workspace, "repo");
        _ = Directory.CreateDirectory(_vaultRoot);
        _ = Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        File.WriteAllText(Path.Combine(_vaultRoot, "note.md"), "hello\n");
        File.WriteAllText(Path.Combine(_repoRoot, ".git", "HEAD"), "ref: refs/heads/feature/AC-3-oc\n");
        File.WriteAllText(Path.Combine(_repoRoot, "AGENTS.md"), "hello\n");

        _registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            taskPattern: 'AC-\d+'
            sources:
              - id: vault
                layer: global
                root: {_vaultRoot}
              - id: repo-kb
                layer: local
                root: {_repoRoot}
            """);
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private JsonObject? MapTool(string tool, JsonObject args)
    {
        JsonObject payload = new()
        {
            ["hook_event_name"] = "tool.execute.after",
            ["session_id"] = "ses_test1",
            ["directory"] = _repoRoot,
            ["tool"] = tool,
            ["args"] = args,
        };
        return OpencodeAdapter.MapToolExecute(payload, _registry, _clock, CryptographicUlidEntropy.Instance);
    }

    [Fact]
    public void Read_MapsToKnowledgeRead_WithTranscriptStampAndTask()
    {
        string notePath = Path.Combine(_vaultRoot, "note.md");
        JsonObject? mapped = MapTool("read", new JsonObject { ["filePath"] = notePath });

        Assert.NotNull(mapped);
        EventValidationResult result = new EventValidator().Validate(mapped.ToJsonString());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal("knowledge.read", (string?)mapped["type"]);
        Assert.Equal("//test-machine/opencode", (string?)mapped["source"]);
        Assert.Equal("vault", (string?)mapped["kbroot"]);
        Assert.Equal("5891b5b522d5df08", (string?)mapped["data"]!["contenthash"]);
        Assert.Equal("AC-3", (string?)mapped["task"]);
        Assert.Equal("hook", (string?)mapped["data"]!["origin"]);
        Assert.Equal("ses_test1", (string?)mapped["data"]!["transcript"]);
        Assert.Equal("ses_test1", (string?)mapped["session"]);
    }

    [Fact]
    public void GrepAndGlob_MapToSearched_UnknownToolsToNothing()
    {
        JsonObject? grep = MapTool("grep", new JsonObject { ["pattern"] = "duckdb", ["path"] = _vaultRoot });
        JsonObject? glob = MapTool("glob", new JsonObject { ["pattern"] = "**/*.md" });
        JsonObject? bash = MapTool("bash", new JsonObject { ["command"] = "ls" });

        Assert.NotNull(grep);
        Assert.Equal("knowledge.searched", (string?)grep["type"]);
        Assert.Equal("vault", (string?)grep["kbroot"]);
        Assert.Null(grep["data"]!["hits"]);
        Assert.NotNull(glob);
        Assert.Equal(_repoRoot, (string?)glob["data"]!["root"]);
        Assert.Null(bash);
        Assert.True(new EventValidator().Validate(grep.ToJsonString()).IsValid);
    }

    [Fact]
    public void WriteAndEdit_MapToWritten()
    {
        string notePath = Path.Combine(_vaultRoot, "new.md");
        JsonObject? written = MapTool("write", new JsonObject { ["filePath"] = notePath });
        JsonObject? edited = MapTool("edit", new JsonObject { ["filePath"] = notePath });

        Assert.Equal("knowledge.written", (string?)written!["type"]);
        Assert.Equal("knowledge.written", (string?)edited!["type"]);
        Assert.Equal("knowledge.written/2", (string?)written!["schemaref"]);
        Assert.Null(written!["data"]!["linkcount"]);
        Assert.True(new EventValidator().Validate(written.ToJsonString()).IsValid);
    }

    [Fact]
    public void Write_ToExistingLinkedNote_CountsLinkcount()
    {
        // per R-002 — the opencode live path computes the same distinct-target
        // count as the Claude Code adapter, through the shared counter.
        string notePath = Path.Combine(_vaultRoot, "linked.md");
        File.WriteAllText(notePath, "[[Alpha]] [[Alpha|alias]] [[Beta#x]] ![[Gamma]] [[Delta]]\n");

        JsonObject? mapped = MapTool("write", new JsonObject { ["filePath"] = notePath });

        Assert.NotNull(mapped);
        Assert.Equal("knowledge.written/2", (string?)mapped!["schemaref"]);
        Assert.Equal(4, (int?)mapped["data"]!["linkcount"]);
    }

    [Fact]
    public void SessionStart_EmitsSessionStartedAndImplicitAgentsMd()
    {
        string globalConfig = Path.Combine(_workspace, "config-opencode");
        _ = Directory.CreateDirectory(globalConfig);
        File.WriteAllText(Path.Combine(globalConfig, "AGENTS.md"), "global rules\n");

        JsonObject payload = new()
        {
            ["hook_event_name"] = "session.start",
            ["session_id"] = "ses_test2",
            ["directory"] = _repoRoot,
        };
        List<JsonObject> events = OpencodeAdapter.MapSessionStart(payload, _registry, _clock, CryptographicUlidEntropy.Instance, globalConfig);

        EventValidator validator = new();
        Assert.All(events, e => Assert.True(validator.Validate(e.ToJsonString()).IsValid));

        JsonObject started = events[0];
        Assert.Equal("session.started", (string?)started["type"]);
        Assert.Equal("feature/AC-3-oc", (string?)started["data"]!["branch"]);
        Assert.Equal("ses_test2", (string?)started["data"]!["transcript"]);

        List<string?> loaded = [.. events.Where(e => (string?)e["type"] is "context.loaded").Select(e => (string?)e["subject"])];
        Assert.Contains(Path.Combine(globalConfig, "AGENTS.md"), loaded, StringComparer.Ordinal);
        Assert.Contains(Path.Combine(_repoRoot, "AGENTS.md"), loaded, StringComparer.Ordinal);

        JsonObject projectAgents = events.Single(e => (string?)e["subject"] == Path.Combine(_repoRoot, "AGENTS.md"));
        Assert.Equal("repo-kb", (string?)projectAgents["kbroot"]);
        Assert.Equal("5891b5b522d5df08", (string?)projectAgents["data"]!["contenthash"]);
    }
}
