using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Adapters.ClaudeCode;
using Kbo.Bronze;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Tests;

public sealed class ClaudeCodeMapPostToolUseTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _vaultRoot;
    private readonly string _repoRoot;
    private readonly KnowledgeRegistry _registry;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly TimeProvider _clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-08-11T15:00:00Z", CultureInfo.InvariantCulture));

    public ClaudeCodeMapPostToolUseTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-adapter-tests").FullName;
        _vaultRoot = Path.Combine(_workspace, "Knowledge");
        _repoRoot = Path.Combine(_workspace, "repo");
        _ = Directory.CreateDirectory(Path.Combine(_vaultRoot, "notes"));
        _ = Directory.CreateDirectory(Path.Combine(_repoRoot, ".git"));
        File.WriteAllText(Path.Combine(_vaultRoot, "notes", "duckdb.md"), "hello\n");
        File.WriteAllText(Path.Combine(_repoRoot, ".git", "HEAD"), "ref: refs/heads/feature/AC-77-capture\n");

        _registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            taskPattern: 'AC-\d+'
            sources:
              - id: vault
                layer: global
                root: {_vaultRoot}
            """);
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private JsonObject? Map(string toolName, JsonObject toolInput, JsonNode? toolResponse = null)
    {
        JsonObject payload = new()
        {
            ["session_id"] = "sess-0001",
            ["cwd"] = _repoRoot,
            ["hook_event_name"] = "PostToolUse",
            ["tool_name"] = toolName,
            ["tool_input"] = toolInput,
            ["tool_response"] = toolResponse ?? new JsonObject(),
        };
        return ClaudeCodeAdapter.MapPostToolUse(payload, _registry, _clock, CryptographicUlidEntropy.Instance);
    }

    [Fact]
    public void Read_UnderVault_ProducesValidKnowledgeReadEvent()
    {
        string notePath = Path.Combine(_vaultRoot, "notes", "duckdb.md");
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
        Assert.Equal(_repoRoot, (string?)mapped["repo"]);
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
        string outsidePath = Path.Combine(_workspace, "elsewhere.md");
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
        string bigPath = Path.Combine(_vaultRoot, "big.canvas");
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
        string notePath = Path.Combine(_vaultRoot, "notes", "duckdb.md");
        JsonObject? mapped = Map("Read", new JsonObject { ["file_path"] = notePath });

        Assert.NotNull(mapped);
        Assert.Equal("hook", (string?)mapped["data"]!["origin"]);
    }

    [Fact]
    public void UnrelatedTool_MapsToNothing() => Assert.Null(Map("Bash", new JsonObject { ["command"] = "ls" }));
}
