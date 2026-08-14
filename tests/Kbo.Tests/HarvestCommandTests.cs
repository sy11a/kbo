using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Cli;
using Kbo.Schemas;

namespace Kbo.Tests;

public class HarvestCommandTests : IDisposable
{
    private readonly string workspace;
    private readonly string transcriptsRoot;
    private readonly string eventsRepo;
    private readonly string registryPath;
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    public HarvestCommandTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-harvest-tests").FullName;
        transcriptsRoot = Path.Combine(workspace, "projects");
        eventsRepo = Path.Combine(workspace, "kb-events");
        registryPath = Path.Combine(workspace, "registry.yaml");
        string vaultRoot = Path.Combine(workspace, "Knowledge");
        Directory.CreateDirectory(vaultRoot);
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

    private void WriteTranscript(string project, string sessionId, string toolName, string filePath, string? fileName = null)
    {
        string directory = Path.Combine(transcriptsRoot, project);
        Directory.CreateDirectory(directory);
        string line = new JsonObject
        {
            ["type"] = "assistant",
            ["sessionId"] = sessionId,
            ["timestamp"] = "2026-07-01T10:00:00.000Z",
            ["cwd"] = workspace,
            ["gitBranch"] = "master",
            ["requestId"] = "req-1",
            ["message"] = new JsonObject
            {
                ["model"] = "claude-fable-5",
                ["role"] = "assistant",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "tool_use",
                    ["id"] = "tu-1",
                    ["name"] = toolName,
                    ["input"] = new JsonObject { ["file_path"] = filePath },
                }),
            },
        }.ToJsonString();
        File.WriteAllText(Path.Combine(directory, (fileName ?? sessionId) + ".jsonl"), line + "\n");
    }

    private void WriteReadAndSkillTranscript(string project, string sessionId, string skillName)
    {
        string directory = Path.Combine(transcriptsRoot, project);
        Directory.CreateDirectory(directory);
        string line = new JsonObject
        {
            ["type"] = "assistant",
            ["sessionId"] = sessionId,
            ["timestamp"] = "2026-07-01T10:00:00.000Z",
            ["cwd"] = workspace,
            ["gitBranch"] = "master",
            ["requestId"] = "req-1",
            ["message"] = new JsonObject
            {
                ["model"] = "claude-fable-5",
                ["role"] = "assistant",
                ["content"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = "tu-read",
                        ["name"] = "Read",
                        ["input"] = new JsonObject { ["file_path"] = Path.Combine(workspace, "Knowledge", "note.md") },
                    },
                    new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = "tu-skill",
                        ["name"] = "Skill",
                        ["input"] = new JsonObject { ["skill"] = skillName },
                    }),
            },
        }.ToJsonString();
        File.WriteAllText(Path.Combine(directory, sessionId + ".jsonl"), line + "\n");
    }

    private int Run(params string[] extraArgs)
    {
        string? Environment(string name) => name switch
        {
            "KBO_REGISTRY" => registryPath,
            "KBO_EVENTS_REPO" => eventsRepo,
            _ => null,
        };
        string[] args = new[] { "claude-code", "--transcripts", transcriptsRoot }.Concat(extraArgs).ToArray();
        return HarvestCommand.Run(args, output, error, Environment, workspace);
    }

    [Fact]
    public void Harvest_MinesAllProjectTranscripts_IntoValidatedBronze()
    {
        WriteTranscript("proj-a", "sess-a", "Read", Path.Combine(workspace, "Knowledge", "note.md"));
        WriteTranscript("proj-b", "sess-b", "Write", Path.Combine(workspace, "elsewhere.md"));

        int exitCode = Run();

        Assert.Equal(0, exitCode);
        string monthFile = Directory.EnumerateFiles(
            Path.Combine(eventsRepo, "bronze", "test-machine", "claude-code")).Single();
        string[] lines = File.ReadAllLines(monthFile);
        EventValidator validator = new();
        Assert.All(lines, line => Assert.True(validator.Validate(line).IsValid));
        Assert.Equal(2, lines.Count(l => l.Contains("\"session.started\"")));
        Assert.Contains(lines, l => l.Contains("\"knowledge.read\"") && l.Contains("sess-a"));
        Assert.Contains(lines, l => l.Contains("\"knowledge.written\"") && l.Contains("sess-b"));
        Assert.Contains("2 session", output.ToString());
    }

    [Fact]
    public void Harvest_Rerun_SkipsAlreadyHarvestedSessions()
    {
        WriteTranscript("proj-a", "sess-a", "Read", Path.Combine(workspace, "Knowledge", "note.md"));
        Run();
        string monthFile = Directory.EnumerateFiles(
            Path.Combine(eventsRepo, "bronze", "test-machine", "claude-code")).Single();
        int linesAfterFirstRun = File.ReadAllLines(monthFile).Length;

        int exitCode = Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(linesAfterFirstRun, File.ReadAllLines(monthFile).Length);
    }

    [Fact]
    public void Harvest_Rerun_SkipsFileWhoseRecordsCarryDifferentSessionId()
    {
        WriteTranscript("proj-a", "sess-original", "Read",
            Path.Combine(workspace, "Knowledge", "note.md"), fileName: "continuation-file");
        Run();
        string monthFile = Directory.EnumerateFiles(
            Path.Combine(eventsRepo, "bronze", "test-machine", "claude-code")).Single();
        int linesAfterFirstRun = File.ReadAllLines(monthFile).Length;

        int exitCode = Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(linesAfterFirstRun, File.ReadAllLines(monthFile).Length);
        Assert.Contains(File.ReadAllLines(monthFile),
            l => l.Contains("\"transcript\":\"continuation-file\"") && l.Contains("sess-original"));
    }

    [Fact]
    public void Harvest_HookOnlySession_IsStillHarvested()
    {
        new BronzeStore(eventsRepo).Append(new[]
        {
            new JsonObject
            {
                ["type"] = "knowledge.read",
                ["time"] = "2026-07-01T09:00:00Z",
                ["machine"] = "test-machine",
                ["agent"] = "claude-code",
                ["session"] = "sess-a",
                ["data"] = new JsonObject { ["origin"] = "hook" },
            },
        });
        WriteTranscript("proj-a", "sess-a", "Read", Path.Combine(workspace, "Knowledge", "note.md"));

        int exitCode = Run();

        Assert.Equal(0, exitCode);
        string monthFile = Directory.EnumerateFiles(
            Path.Combine(eventsRepo, "bronze", "test-machine", "claude-code")).Single();
        Assert.Contains(File.ReadAllLines(monthFile), l => l.Contains("\"origin\":\"harvest\"") && l.Contains("sess-a"));
    }

    [Fact]
    public void Harvest_Normal_EmitsSkillInvoked_FromTheSkillTool()
    {
        WriteReadAndSkillTranscript("proj-a", "sess-a", "tdd");

        Assert.Equal(0, Run());

        string monthFile = Directory.EnumerateFiles(
            Path.Combine(eventsRepo, "bronze", "test-machine", "claude-code")).Single();
        Assert.Contains(File.ReadAllLines(monthFile),
            l => l.Contains("\"skill.invoked\"") && l.Contains("\"skill\":\"tdd\""));
    }

    [Fact]
    public void BackfillSkills_AddsOnlySkillInvoked_ToAlreadyHarvestedTranscripts_Idempotently()
    {
        WriteReadAndSkillTranscript("proj-a", "sess-a", "tdd");
        // Simulate a pre-skill harvest: the transcript is already harvested but carries no skill.invoked.
        new BronzeStore(eventsRepo).Append(new[]
        {
            new JsonObject
            {
                ["type"] = "knowledge.read",
                ["time"] = "2026-07-01T09:00:00Z",
                ["subject"] = "/x.md",
                ["machine"] = "test-machine",
                ["agent"] = "claude-code",
                ["session"] = "sess-a",
                ["data"] = new JsonObject { ["origin"] = "harvest", ["transcript"] = "sess-a" },
            },
        });

        Assert.Equal(0, Run("--backfill-skills"));

        string monthFile = Directory.EnumerateFiles(
            Path.Combine(eventsRepo, "bronze", "test-machine", "claude-code")).Single();
        string[] afterBackfill = File.ReadAllLines(monthFile);
        Assert.Single(afterBackfill, l => l.Contains("\"skill.invoked\"") && l.Contains("\"skill\":\"tdd\""));
        // The read/session events were NOT re-mined (only skill.invoked is additive).
        Assert.DoesNotContain(afterBackfill, l => l.Contains("\"session.started\""));

        Assert.Equal(0, Run("--backfill-skills"));
        Assert.Equal(afterBackfill.Length, File.ReadAllLines(monthFile).Length);
    }

    [Fact]
    public void Harvest_NoTranscriptsDirectory_FailsWithError()
    {
        int exitCode = HarvestCommand.Run(
            new[] { "claude-code", "--transcripts", Path.Combine(workspace, "missing") },
            output, error, _ => registryPath, workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("missing", error.ToString());
    }
}
