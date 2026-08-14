using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Adapters.ClaudeCode;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Tests;

public class ClaudeCodeMapPostToolUseTests : IDisposable
{
    private readonly string workspace;
    private readonly string vaultRoot;
    private readonly string repoRoot;
    private readonly KnowledgeRegistry registry;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly TimeProvider Clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-08-11T15:00:00Z", CultureInfo.InvariantCulture));

    public ClaudeCodeMapPostToolUseTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-adapter-tests").FullName;
        vaultRoot = Path.Combine(workspace, "Knowledge");
        repoRoot = Path.Combine(workspace, "repo");
        Directory.CreateDirectory(Path.Combine(vaultRoot, "notes"));
        Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
        File.WriteAllText(Path.Combine(vaultRoot, "notes", "duckdb.md"), "hello\n");
        File.WriteAllText(Path.Combine(repoRoot, ".git", "HEAD"), "ref: refs/heads/feature/AC-77-capture\n");

        registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            taskPattern: 'AC-\d+'
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
            ["cwd"] = repoRoot,
            ["hook_event_name"] = "PostToolUse",
            ["tool_name"] = toolName,
            ["tool_input"] = toolInput,
            ["tool_response"] = toolResponse ?? new JsonObject(),
        };
        return ClaudeCodeAdapter.MapPostToolUse(payload, registry, Clock, new Random(42));
    }

    [Fact]
    public void Read_UnderVault_ProducesValidKnowledgeReadEvent()
    {
        string notePath = Path.Combine(vaultRoot, "notes", "duckdb.md");
        JsonObject? mapped = Map("Read", new JsonObject { ["file_path"] = notePath });

        Assert.NotNull(mapped);
        EventValidationResult result = new EventValidator().Validate(mapped.ToJsonString());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));

        Assert.Equal("knowledge.read", (string?)mapped["type"]);
        Assert.Equal("knowledge.read/1", (string?)mapped["schemaref"]);
        Assert.Equal("//test-machine/claude-code", (string?)mapped["source"]);
        Assert.Equal(notePath, (string?)mapped["subject"]);
        Assert.Equal("vault", (string?)mapped["kbroot"]);
        Assert.Equal("sess-0001", (string?)mapped["session"]);
        Assert.Equal(repoRoot, (string?)mapped["repo"]);
        Assert.Equal("AC-77", (string?)mapped["task"]);
        Assert.Null(mapped["model"]);
        Assert.Equal("2026-08-11T15:00:00Z", (string?)mapped["time"]);

        Assert.Equal("5891b5b522d5df08", (string?)mapped["data"]!["contenthash"]);
        Assert.Equal(notePath, (string?)mapped["data"]!["path"]);
        Assert.Equal("Read", (string?)mapped["data"]!["raw"]!["tool_name"]);
        Assert.Null(mapped["data"]!["raw"]!.AsObject()["tool_response"]);
    }

    [Fact]
    public void Read_OutsideRoots_HasNullKbrootAndNoHash()
    {
        string outsidePath = Path.Combine(workspace, "elsewhere.md");
        File.WriteAllText(outsidePath, "hello\n");
        JsonObject? mapped = Map("Read", new JsonObject { ["file_path"] = outsidePath });

        Assert.NotNull(mapped);
        Assert.True(new EventValidator().Validate(mapped.ToJsonString()).IsValid);
        Assert.Null(mapped["kbroot"]);
        Assert.Null(mapped["data"]!["contenthash"]);
    }

    [Fact]
    public void Read_LargeVaultFile_RecordsSizeInsteadOfHash()
    {
        string bigPath = Path.Combine(vaultRoot, "big.canvas");
        using (FileStream stream = File.Create(bigPath))
        {
            stream.SetLength(6 * 1024 * 1024);
        }
        JsonObject? mapped = Map("Read", new JsonObject { ["file_path"] = bigPath });

        Assert.NotNull(mapped);
        Assert.True(new EventValidator().Validate(mapped.ToJsonString()).IsValid);
        Assert.Null(mapped["data"]!["contenthash"]);
        Assert.Equal(6 * 1024 * 1024, (long?)mapped["data"]!["size"]);
    }

    [Fact]
    public void MappedEvents_CarryHookOrigin()
    {
        string notePath = Path.Combine(vaultRoot, "notes", "duckdb.md");
        JsonObject? mapped = Map("Read", new JsonObject { ["file_path"] = notePath });

        Assert.NotNull(mapped);
        Assert.Equal("hook", (string?)mapped["data"]!["origin"]);
    }

    [Fact]
    public void UnrelatedTool_MapsToNothing()
    {
        Assert.Null(Map("Bash", new JsonObject { ["command"] = "ls" }));
    }
}
