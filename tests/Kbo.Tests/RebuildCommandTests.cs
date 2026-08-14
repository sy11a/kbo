using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Cli;

namespace Kbo.Tests;

public class RebuildCommandTests : IDisposable
{
    private readonly string workspace;
    private readonly string eventsRepo;
    private readonly string silverPath;
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    public RebuildCommandTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-rebuild-tests").FullName;
        eventsRepo = Path.Combine(workspace, "kb-events");
        silverPath = Path.Combine(workspace, "data", "silver.duckdb");

        new BronzeStore(eventsRepo).Append(new[]
        {
            new JsonObject
            {
                ["id"] = "01A00000000000000000000001",
                ["type"] = "session.started",
                ["time"] = "2026-07-01T10:00:00Z",
                ["machine"] = "test-machine",
                ["agent"] = "claude-code",
                ["session"] = "sess-1",
                ["data"] = new JsonObject { ["origin"] = "harvest", ["transcript"] = "file-1" },
            },
        });
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private int Run(params string[] args)
    {
        string? Environment(string name) => name switch
        {
            "KBO_EVENTS_REPO" => eventsRepo,
            "KBO_SILVER" => silverPath,
            _ => null,
        };
        return RebuildCommand.Run(args, output, error, Environment, workspace);
    }

    [Fact]
    public void Rebuild_CreatesSilverAndReportsCounts()
    {
        int exitCode = Run();

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(silverPath));
        Assert.Contains("1 event", output.ToString());
        Assert.Contains("1 session", output.ToString());
    }

    [Fact]
    public void Rebuild_RunTwice_Succeeds()
    {
        Assert.Equal(0, Run());
        Assert.Equal(0, Run());
    }

    [Fact]
    public void Rebuild_MissingEventsRepo_FailsWithError()
    {
        int exitCode = RebuildCommand.Run(
            new[] { "--events-repo", Path.Combine(workspace, "nope") },
            output, error, _ => silverPath, workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("nope", error.ToString());
    }
}
