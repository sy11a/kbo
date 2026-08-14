using System.Text.Json.Nodes;
using Kbo.Adapters.ClaudeCode;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Tests;

public class TranscriptMinerTests : IDisposable
{
    private readonly string workspace;
    private readonly string vaultRoot;
    private readonly KnowledgeRegistry registry;

    public TranscriptMinerTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-miner-tests").FullName;
        vaultRoot = Path.Combine(workspace, "Knowledge");
        Directory.CreateDirectory(vaultRoot);
        registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {vaultRoot}
            """);
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private static string MetaJson(string type, string timestamp, string cwd, string branch)
    {
        return new JsonObject
        {
            ["type"] = type,
            ["sessionId"] = "sess-harvest-1",
            ["timestamp"] = timestamp,
            ["cwd"] = cwd,
            ["gitBranch"] = branch,
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = "hello" },
        }.ToJsonString();
    }

    private string AssistantToolUse(string toolName, JsonObject input, string toolUseId, string requestId, JsonObject? usage = null)
    {
        return new JsonObject
        {
            ["type"] = "assistant",
            ["sessionId"] = "sess-harvest-1",
            ["timestamp"] = "2026-07-01T10:01:00.500Z",
            ["cwd"] = workspace,
            ["gitBranch"] = "feature/AC-12-reports",
            ["requestId"] = requestId,
            ["message"] = new JsonObject
            {
                ["model"] = "claude-fable-5",
                ["role"] = "assistant",
                ["usage"] = usage,
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "tool_use",
                    ["id"] = toolUseId,
                    ["name"] = toolName,
                    ["input"] = input,
                }),
            },
        }.ToJsonString();
    }

    private static string ToolResult(string toolUseId, JsonNode? toolUseResult)
    {
        return new JsonObject
        {
            ["type"] = "user",
            ["sessionId"] = "sess-harvest-1",
            ["timestamp"] = "2026-07-01T10:01:01.000Z",
            ["toolUseResult"] = toolUseResult,
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = toolUseId,
                }),
            },
        }.ToJsonString();
    }

    private List<JsonObject> MineSample()
    {
        string vaultNote = Path.Combine(vaultRoot, "note.md");
        List<string> lines = new()
        {
            MetaJson("user", "2026-07-01T10:00:00.000Z", workspace, "feature/AC-12-reports"),
            "{{{ not json at all",
            AssistantToolUse("Read", new JsonObject { ["file_path"] = vaultNote }, "tu-1", "req-1",
                new JsonObject { ["input_tokens"] = 100, ["cache_read_input_tokens"] = 1000, ["output_tokens"] = 10 }),
            AssistantToolUse("Grep", new JsonObject { ["pattern"] = "duckdb", ["path"] = vaultRoot }, "tu-2", "req-1",
                new JsonObject { ["input_tokens"] = 100, ["cache_read_input_tokens"] = 1000, ["output_tokens"] = 10 }),
            ToolResult("tu-2", new JsonObject
            {
                ["mode"] = "files_with_matches",
                ["numFiles"] = 4,
                ["filenames"] = new JsonArray("a.md", "b.md", "c.md", "d.md"),
            }),
            AssistantToolUse("Write", new JsonObject { ["file_path"] = vaultNote }, "tu-3", "req-2",
                new JsonObject { ["input_tokens"] = 50, ["cache_read_input_tokens"] = 500, ["output_tokens"] = 5 }),
            AssistantToolUse("Bash", new JsonObject { ["command"] = "ls" }, "tu-4", "req-3"),
        };
        return TranscriptMiner.Mine(lines, "fallback-session", registry, new Random(42));
    }

    [Fact]
    public void Mine_EmitsSessionStarted_WithHistoricalBranchModelAndDedupedUsage()
    {
        List<JsonObject> events = MineSample();

        JsonObject started = events[0];
        EventValidationResult result = new EventValidator().Validate(started.ToJsonString());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal("session.started", (string?)started["type"]);
        Assert.Equal("sess-harvest-1", (string?)started["session"]);
        Assert.Equal("sess-harvest-1", (string?)started["subject"]);
        Assert.Equal("2026-07-01T10:00:00Z", (string?)started["time"]);
        Assert.Equal("feature/AC-12-reports", (string?)started["data"]!["branch"]);
        Assert.Equal("AC-12", (string?)started["task"]);
        Assert.Equal("claude-fable-5", (string?)started["model"]);
        Assert.Equal("harvest", (string?)started["data"]!["origin"]);

        JsonObject usage = started["data"]!["usage"]!.AsObject();
        Assert.Equal(150, (long?)usage["input_tokens"]);
        Assert.Equal(1500, (long?)usage["cache_read_tokens"]);
        Assert.Equal(15, (long?)usage["output_tokens"]);
    }

    [Fact]
    public void Mine_EmitsToolEvents_WithModelAndHarvestOrigin()
    {
        List<JsonObject> events = MineSample();
        EventValidator validator = new();

        List<string?> types = events.Select(e => (string?)e["type"]).ToList();
        Assert.Equal(new[] { "session.started", "knowledge.read", "knowledge.searched", "knowledge.written" }, types);

        foreach (JsonObject minedEvent in events)
        {
            EventValidationResult result = validator.Validate(minedEvent.ToJsonString());
            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Equal("harvest", (string?)minedEvent["data"]!["origin"]);
        }

        JsonObject read = events[1];
        Assert.Equal("vault", (string?)read["kbroot"]);
        Assert.Equal("claude-fable-5", (string?)read["model"]);
        Assert.Null(read["data"]!["contenthash"]);
        Assert.Equal("2026-07-01T10:01:00Z", (string?)read["time"]);
    }

    [Fact]
    public void Mine_SearchHits_AreAuthoritativeFromToolUseResult()
    {
        List<JsonObject> events = MineSample();

        JsonObject searched = events.Single(e => (string?)e["type"] == "knowledge.searched");
        Assert.Equal(4, (int?)searched["data"]!["hits"]);
        Assert.Equal("vault", (string?)searched["kbroot"]);
    }

    [Fact]
    public void Mine_EmitsSkillInvoked_ForTheSkillTool()
    {
        List<string> lines = new()
        {
            MetaJson("user", "2026-07-01T10:00:00.000Z", workspace, "feature/AC-12-reports"),
            AssistantToolUse("Skill", new JsonObject { ["skill"] = "tdd" }, "tu-s", "req-1"),
        };

        List<JsonObject> events = TranscriptMiner.Mine(lines, "fallback-session", registry, new Random(42));

        JsonObject skill = Assert.Single(events, e => (string?)e["type"] == "skill.invoked");
        EventValidationResult result = new EventValidator().Validate(skill.ToJsonString());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal("tdd", (string?)skill["subject"]);
        Assert.Equal("tdd", (string?)skill["data"]!["skill"]);
        Assert.Equal("harvest", (string?)skill["data"]!["origin"]);
        Assert.Equal("fallback-session", (string?)skill["data"]!["transcript"]);
    }

    [Fact]
    public void Mine_WrittenEvent_StripsContentFromRaw_KeepsContenthashNull()
    {
        string vaultNote = Path.Combine(vaultRoot, "mined-note.md");
        List<string> lines = new()
        {
            MetaJson("user", "2026-07-01T10:00:00.000Z", workspace, "feature/AC-12-reports"),
            AssistantToolUse("Write", new JsonObject
            {
                ["file_path"] = vaultNote,
                ["content"] = "historical body",
            }, "tu-w", "req-1"),
        };

        List<JsonObject> events = TranscriptMiner.Mine(lines, "fallback-session", registry, new Random(42));

        JsonObject written = Assert.Single(events, e => (string?)e["type"] == "knowledge.written");
        EventValidationResult result = new EventValidator().Validate(written.ToJsonString());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        JsonObject rawInput = (JsonObject)written["data"]!["raw"]!["tool_input"]!;
        Assert.False(rawInput.ContainsKey("content"));
        Assert.Equal(15, (int?)rawInput["content_size"]);
        Assert.True(written["data"]!.AsObject().ContainsKey("contenthash"));
        Assert.Null(written["data"]!["contenthash"]);
    }

    [Fact]
    public void Mine_EmptyTranscript_YieldsNoEvents()
    {
        Assert.Empty(TranscriptMiner.Mine(new List<string>(), "fallback", registry, new Random(42)));
    }
}
