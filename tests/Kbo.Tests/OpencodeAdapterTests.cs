using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Adapters.Opencode;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Tests;

public class OpencodeAdapterTests : IDisposable
{
    private readonly string workspace;
    private readonly string vaultRoot;
    private readonly string repoRoot;
    private readonly KnowledgeRegistry registry;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly TimeProvider Clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-08-12T23:30:00Z", CultureInfo.InvariantCulture));

    public OpencodeAdapterTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-oc-adapter-tests").FullName;
        vaultRoot = Path.Combine(workspace, "Knowledge");
        repoRoot = Path.Combine(workspace, "repo");
        Directory.CreateDirectory(vaultRoot);
        Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
        File.WriteAllText(Path.Combine(vaultRoot, "note.md"), "hello\n");
        File.WriteAllText(Path.Combine(repoRoot, ".git", "HEAD"), "ref: refs/heads/feature/AC-3-oc\n");
        File.WriteAllText(Path.Combine(repoRoot, "AGENTS.md"), "hello\n");

        registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {vaultRoot}
              - id: repo-kb
                layer: local
                root: {repoRoot}
            """);
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private JsonObject? MapTool(string tool, JsonObject args)
    {
        JsonObject payload = new()
        {
            ["hook_event_name"] = "tool.execute.after",
            ["session_id"] = "ses_test1",
            ["directory"] = repoRoot,
            ["tool"] = tool,
            ["args"] = args,
        };
        return OpencodeAdapter.MapToolExecute(payload, registry, Clock, new Random(42));
    }

    [Fact]
    public void Read_MapsToKnowledgeRead_WithTranscriptStampAndTask()
    {
        string notePath = Path.Combine(vaultRoot, "note.md");
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
        JsonObject? grep = MapTool("grep", new JsonObject { ["pattern"] = "duckdb", ["path"] = vaultRoot });
        JsonObject? glob = MapTool("glob", new JsonObject { ["pattern"] = "**/*.md" });
        JsonObject? bash = MapTool("bash", new JsonObject { ["command"] = "ls" });

        Assert.NotNull(grep);
        Assert.Equal("knowledge.searched", (string?)grep["type"]);
        Assert.Equal("vault", (string?)grep["kbroot"]);
        Assert.Null(grep["data"]!["hits"]);
        Assert.NotNull(glob);
        Assert.Equal(repoRoot, (string?)glob["data"]!["root"]);
        Assert.Null(bash);
        Assert.True(new EventValidator().Validate(grep.ToJsonString()).IsValid);
    }

    [Fact]
    public void WriteAndEdit_MapToWritten()
    {
        string notePath = Path.Combine(vaultRoot, "new.md");
        JsonObject? written = MapTool("write", new JsonObject { ["filePath"] = notePath });
        JsonObject? edited = MapTool("edit", new JsonObject { ["filePath"] = notePath });

        Assert.Equal("knowledge.written", (string?)written!["type"]);
        Assert.Equal("knowledge.written", (string?)edited!["type"]);
        Assert.True(new EventValidator().Validate(written.ToJsonString()).IsValid);
    }

    [Fact]
    public void SessionStart_EmitsSessionStartedAndImplicitAgentsMd()
    {
        string globalConfig = Path.Combine(workspace, "config-opencode");
        Directory.CreateDirectory(globalConfig);
        File.WriteAllText(Path.Combine(globalConfig, "AGENTS.md"), "global rules\n");

        JsonObject payload = new()
        {
            ["hook_event_name"] = "session.start",
            ["session_id"] = "ses_test2",
            ["directory"] = repoRoot,
        };
        List<JsonObject> events = OpencodeAdapter.MapSessionStart(payload, registry, Clock, new Random(42), globalConfig);

        EventValidator validator = new();
        Assert.All(events, e => Assert.True(validator.Validate(e.ToJsonString()).IsValid));

        JsonObject started = events[0];
        Assert.Equal("session.started", (string?)started["type"]);
        Assert.Equal("feature/AC-3-oc", (string?)started["data"]!["branch"]);
        Assert.Equal("ses_test2", (string?)started["data"]!["transcript"]);

        List<string?> loaded = events.Where(e => (string?)e["type"] == "context.loaded")
            .Select(e => (string?)e["subject"]).ToList();
        Assert.Contains(Path.Combine(globalConfig, "AGENTS.md"), loaded);
        Assert.Contains(Path.Combine(repoRoot, "AGENTS.md"), loaded);

        JsonObject projectAgents = events.Single(e => (string?)e["subject"] == Path.Combine(repoRoot, "AGENTS.md"));
        Assert.Equal("repo-kb", (string?)projectAgents["kbroot"]);
        Assert.Equal("5891b5b522d5df08", (string?)projectAgents["data"]!["contenthash"]);
    }
}
