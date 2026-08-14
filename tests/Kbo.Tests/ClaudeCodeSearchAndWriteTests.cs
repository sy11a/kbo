using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Adapters.ClaudeCode;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Tests;

public class ClaudeCodeSearchAndWriteTests : IDisposable
{
    private readonly string workspace;
    private readonly string vaultRoot;
    private readonly KnowledgeRegistry registry;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly TimeProvider Clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-08-11T15:00:00Z", CultureInfo.InvariantCulture));

    public ClaudeCodeSearchAndWriteTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-adapter-tests").FullName;
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

    private JsonObject? Map(string toolName, JsonObject toolInput, JsonNode? toolResponse = null)
    {
        JsonObject payload = new()
        {
            ["session_id"] = "sess-0001",
            ["cwd"] = workspace,
            ["hook_event_name"] = "PostToolUse",
            ["tool_name"] = toolName,
            ["tool_input"] = toolInput,
            ["tool_response"] = toolResponse ?? new JsonObject(),
        };
        return ClaudeCodeAdapter.MapPostToolUse(payload, registry, Clock, new Random(42));
    }

    private static void AssertValid(JsonObject mapped)
    {
        EventValidationResult result = new EventValidator().Validate(mapped.ToJsonString());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Grep_OverVault_ProducesSearchedEventWithHitCount()
    {
        JsonObject? mapped = Map(
            "Grep",
            new JsonObject { ["pattern"] = "duckdb appender", ["path"] = vaultRoot },
            new JsonObject { ["numFiles"] = 3 });

        Assert.NotNull(mapped);
        AssertValid(mapped);
        Assert.Equal("knowledge.searched", (string?)mapped["type"]);
        Assert.Equal("duckdb appender", (string?)mapped["subject"]);
        Assert.Equal("vault", (string?)mapped["kbroot"]);
        Assert.Equal(vaultRoot, (string?)mapped["data"]!["root"]);
        Assert.Equal(3, (int?)mapped["data"]!["hits"]);
    }

    [Fact]
    public void Grep_WithoutPath_UsesCwdAsRootAndNullHits()
    {
        JsonObject? mapped = Map("Grep", new JsonObject { ["pattern"] = "retention" }, JsonValue.Create("no matches"));

        Assert.NotNull(mapped);
        AssertValid(mapped);
        Assert.Equal(workspace, (string?)mapped["data"]!["root"]);
        Assert.Null(mapped["data"]!["hits"]);
        Assert.Null(mapped["kbroot"]);
    }

    [Fact]
    public void Glob_OverVault_ProducesSearchedEvent()
    {
        JsonObject? mapped = Map(
            "Glob",
            new JsonObject { ["pattern"] = "**/*.md", ["path"] = vaultRoot },
            new JsonObject { ["numFiles"] = 12 });

        Assert.NotNull(mapped);
        AssertValid(mapped);
        Assert.Equal("knowledge.searched", (string?)mapped["type"]);
        Assert.Equal("vault", (string?)mapped["kbroot"]);
        Assert.Equal(12, (int?)mapped["data"]!["hits"]);
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("Edit")]
    [InlineData("NotebookEdit")]
    public void WriteLikeTools_ProduceWrittenEvent(string toolName)
    {
        string notePath = Path.Combine(vaultRoot, "new-note.md");
        string inputKey = toolName == "NotebookEdit" ? "notebook_path" : "file_path";
        JsonObject? mapped = Map(toolName, new JsonObject { [inputKey] = notePath });

        Assert.NotNull(mapped);
        AssertValid(mapped);
        Assert.Equal("knowledge.written", (string?)mapped["type"]);
        Assert.Equal(notePath, (string?)mapped["subject"]);
        Assert.Equal("vault", (string?)mapped["kbroot"]);
        Assert.Equal(notePath, (string?)mapped["data"]!["path"]);
    }
}
