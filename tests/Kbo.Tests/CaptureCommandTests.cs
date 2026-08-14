using System.Text.Json.Nodes;
using Kbo.Cli;
using Kbo.Schemas;

namespace Kbo.Tests;

public class CaptureCommandTests : IDisposable
{
    private readonly string workspace;
    private readonly string vaultRoot;
    private readonly string eventsRepo;
    private readonly string registryPath;
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    public CaptureCommandTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-capture-tests").FullName;
        vaultRoot = Path.Combine(workspace, "Knowledge");
        eventsRepo = Path.Combine(workspace, "kb-events");
        Directory.CreateDirectory(vaultRoot);
        File.WriteAllText(Path.Combine(vaultRoot, "note.md"), "hello\n");

        registryPath = Path.Combine(workspace, "registry.yaml");
        File.WriteAllText(registryPath, $"""
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

    private string CaptureLog => Path.Combine(workspace, ".local", "state", "kbo", "capture-errors.log");

    private int Run(JsonObject payload)
    {
        string? Environment(string name) => name switch
        {
            "KBO_REGISTRY" => registryPath,
            "KBO_EVENTS_REPO" => eventsRepo,
            _ => null,
        };
        using StringReader input = new(payload.ToJsonString());
        return CaptureCommand.Run(new[] { "claude-code" }, input, output, error, Environment, workspace);
    }

    [Fact]
    public void PostToolUseRead_LandsValidatedEventInBronze()
    {
        int exitCode = Run(new JsonObject
        {
            ["session_id"] = "sess-cli-1",
            ["cwd"] = workspace,
            ["hook_event_name"] = "PostToolUse",
            ["tool_name"] = "Read",
            ["tool_input"] = new JsonObject { ["file_path"] = Path.Combine(vaultRoot, "note.md") },
            ["tool_response"] = new JsonObject { ["file"] = new JsonObject() },
        });

        Assert.Equal(0, exitCode);
        string monthFile = Directory.EnumerateFiles(
            Path.Combine(eventsRepo, "bronze", "test-machine", "claude-code")).Single();
        string line = File.ReadAllLines(monthFile).Single();
        Assert.True(new EventValidator().Validate(line).IsValid);
        Assert.Contains("\"knowledge.read\"", line);
        Assert.DoesNotContain("tool_response", line);
        Assert.False(File.Exists(CaptureLog));
    }

    [Fact]
    public void SessionStart_LandsSessionStartedEvent()
    {
        int exitCode = Run(new JsonObject
        {
            ["session_id"] = "sess-cli-2",
            ["cwd"] = workspace,
            ["hook_event_name"] = "SessionStart",
            ["source"] = "startup",
        });

        Assert.Equal(0, exitCode);
        string monthFile = Directory.EnumerateFiles(
            Path.Combine(eventsRepo, "bronze", "test-machine", "claude-code")).Single();
        Assert.Contains(File.ReadAllLines(monthFile), l => l.Contains("\"session.started\""));
    }

    [Fact]
    public void UntrackedTool_IsANoOp()
    {
        int exitCode = Run(new JsonObject
        {
            ["session_id"] = "sess-cli-3",
            ["cwd"] = workspace,
            ["hook_event_name"] = "PostToolUse",
            ["tool_name"] = "Bash",
            ["tool_input"] = new JsonObject { ["command"] = "ls" },
        });

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(Path.Combine(eventsRepo, "bronze")));
    }

    [Fact]
    public void MalformedPayload_IsLoggedAndDoesNotFailSession()
    {
        using StringReader input = new("this is not json");
        int exitCode = CaptureCommand.Run(
            new[] { "claude-code" }, input, output, error, _ => registryPath, workspace);

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(Path.Combine(eventsRepo, "bronze")));
        Assert.True(File.Exists(CaptureLog));
        Assert.Contains("claude-code", File.ReadAllText(CaptureLog));
    }

    [Fact]
    public void MissingRegistry_IsLoggedAndDoesNotFailSession()
    {
        string? Environment(string name) => name switch
        {
            "KBO_REGISTRY" => Path.Combine(workspace, "does-not-exist.yaml"),
            "KBO_EVENTS_REPO" => eventsRepo,
            _ => null,
        };
        JsonObject payload = new()
        {
            ["session_id"] = "sess-cli-registry",
            ["cwd"] = workspace,
            ["hook_event_name"] = "PostToolUse",
            ["tool_name"] = "Read",
            ["tool_input"] = new JsonObject { ["file_path"] = Path.Combine(vaultRoot, "note.md") },
        };
        using StringReader input = new(payload.ToJsonString());
        int exitCode = CaptureCommand.Run(new[] { "claude-code" }, input, output, error, Environment, workspace);

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(Path.Combine(eventsRepo, "bronze")));
        Assert.True(File.Exists(CaptureLog));
    }

    [Fact]
    public void UnknownAgent_FailsWithUsage()
    {
        using StringReader input = new("{}");
        int exitCode = CaptureCommand.Run(
            new[] { "some-agent" }, input, output, error, _ => null, workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("claude-code", error.ToString());
    }
}
