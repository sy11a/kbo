using System.Text.Json.Nodes;
using Kbo.Cli;
using Kbo.Schemas;

namespace Kbo.Tests;

public sealed class CaptureCommandTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _vaultRoot;
    private readonly string _eventsRepo;
    private readonly string _registryPath;
    private readonly StringWriter _error = new();

    public CaptureCommandTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-capture-tests").FullName;
        _vaultRoot = Path.Combine(_workspace, "Knowledge");
        _eventsRepo = Path.Combine(_workspace, "kb-events");
        _ = Directory.CreateDirectory(_vaultRoot);
        File.WriteAllText(Path.Combine(_vaultRoot, "note.md"), "hello\n");

        _registryPath = Path.Combine(_workspace, "registry.yaml");
        File.WriteAllText(_registryPath, $"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {_vaultRoot}
            """);
    }

    public void Dispose()
    {
        _error.Dispose();
        Directory.Delete(_workspace, recursive: true);
    }

    private string CaptureLog => Path.Combine(_workspace, ".local", "state", "kbo", "capture-errors.log");

    private int Run(JsonObject payload)
    {
        string? Environment(string name)
        {
            return name switch
            {
                "KBO_REGISTRY" => _registryPath,
                "KBO_EVENTS_REPO" => _eventsRepo,
                _ => null,
            };
        }
        using StringReader input = new(payload.ToJsonString());
        return CaptureCommand.Run(["claude-code"], input, _error, variable => Environment(variable), _workspace);
    }

    [Fact]
    public void PostToolUseRead_LandsValidatedEventInBronze()
    {
        int exitCode = Run(new JsonObject
        {
            ["session_id"] = "sess-cli-1",
            ["cwd"] = _workspace,
            ["hook_event_name"] = "PostToolUse",
            ["tool_name"] = "Read",
            ["tool_input"] = new JsonObject { ["file_path"] = Path.Combine(_vaultRoot, "note.md") },
            ["tool_response"] = new JsonObject { ["file"] = new JsonObject() },
        });

        Assert.Equal(0, exitCode);
        string monthFile = Directory.EnumerateFiles(
            Path.Combine(_eventsRepo, "bronze", "test-machine", "claude-code")).Single();
        string line = File.ReadAllLines(monthFile).Single();
        Assert.True(new EventValidator().Validate(line).IsValid);
        Assert.Contains("\"knowledge.read\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain("tool_response", line, StringComparison.Ordinal);
        Assert.False(File.Exists(CaptureLog));
    }

    [Fact]
    public void SessionStart_LandsSessionStartedEvent()
    {
        int exitCode = Run(new JsonObject
        {
            ["session_id"] = "sess-cli-2",
            ["cwd"] = _workspace,
            ["hook_event_name"] = "SessionStart",
            ["source"] = "startup",
        });

        Assert.Equal(0, exitCode);
        string monthFile = Directory.EnumerateFiles(
            Path.Combine(_eventsRepo, "bronze", "test-machine", "claude-code")).Single();
        Assert.Contains(File.ReadAllLines(monthFile), l => l.Contains("\"session.started\"", StringComparison.Ordinal));
    }

    [Fact]
    public void UntrackedTool_IsANoOp()
    {
        int exitCode = Run(new JsonObject
        {
            ["session_id"] = "sess-cli-3",
            ["cwd"] = _workspace,
            ["hook_event_name"] = "PostToolUse",
            ["tool_name"] = "Bash",
            ["tool_input"] = new JsonObject { ["command"] = "ls" },
        });

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(Path.Combine(_eventsRepo, "bronze")));
    }

    [Fact]
    public void MalformedPayload_IsLoggedAndDoesNotFailSession()
    {
        using StringReader input = new("this is not json");
        int exitCode = CaptureCommand.Run(
            ["claude-code"], input, _error, _ => _registryPath, _workspace);

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(Path.Combine(_eventsRepo, "bronze")));
        Assert.True(File.Exists(CaptureLog));
        Assert.Contains("claude-code", File.ReadAllText(CaptureLog), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRegistry_IsLoggedAndDoesNotFailSession()
    {
        string? Environment(string name)
        {
            return name switch
            {
                "KBO_REGISTRY" => Path.Combine(_workspace, "does-not-exist.yaml"),
                "KBO_EVENTS_REPO" => _eventsRepo,
                _ => null,
            };
        }
        JsonObject payload = new()
        {
            ["session_id"] = "sess-cli-registry",
            ["cwd"] = _workspace,
            ["hook_event_name"] = "PostToolUse",
            ["tool_name"] = "Read",
            ["tool_input"] = new JsonObject { ["file_path"] = Path.Combine(_vaultRoot, "note.md") },
        };
        using StringReader input = new(payload.ToJsonString());
        int exitCode = CaptureCommand.Run(["claude-code"], input, _error, variable => Environment(variable), _workspace);

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(Path.Combine(_eventsRepo, "bronze")));
        Assert.True(File.Exists(CaptureLog));
    }

    [Fact]
    public void MalformedRegistry_IsLoggedAndDoesNotFailSession()
    {
        string malformedRegistryPath = Path.Combine(_workspace, "malformed-registry.yaml");
        File.WriteAllText(malformedRegistryPath, "sources: [this is not: valid: yaml");
        string? Environment(string name)
        {
            return name switch
            {
                "KBO_REGISTRY" => malformedRegistryPath,
                "KBO_EVENTS_REPO" => _eventsRepo,
                _ => null,
            };
        }
        JsonObject payload = new()
        {
            ["session_id"] = "sess-cli-malformed-registry",
            ["cwd"] = _workspace,
            ["hook_event_name"] = "PostToolUse",
            ["tool_name"] = "Read",
            ["tool_input"] = new JsonObject { ["file_path"] = Path.Combine(_vaultRoot, "note.md") },
        };
        using StringReader input = new(payload.ToJsonString());
        int exitCode = CaptureCommand.Run(["claude-code"], input, _error, variable => Environment(variable), _workspace);

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(Path.Combine(_eventsRepo, "bronze")));
        Assert.True(File.Exists(CaptureLog));
        Assert.Contains("claude-code", File.ReadAllText(CaptureLog), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownAgent_FailsWithUsage()
    {
        using StringReader input = new("{}");
        int exitCode = CaptureCommand.Run(
            ["some-agent"], input, _error, _ => null, _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("claude-code", _error.ToString(), StringComparison.Ordinal);
    }
}
