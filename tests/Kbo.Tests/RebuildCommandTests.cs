using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Cli;

namespace Kbo.Tests;

public sealed class RebuildCommandTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _eventsRepo;
    private readonly string _silverPath;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public RebuildCommandTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-rebuild-tests").FullName;
        _eventsRepo = Path.Combine(_workspace, "kb-events");
        _silverPath = Path.Combine(_workspace, "data", "silver.duckdb");

        new BronzeStore(_eventsRepo).Append(new[]
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
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_workspace, recursive: true);
    }

    private int Run(params string[] args)
    {
        string? Environment(string name)
        {
            return name switch
            {
                "KBO_EVENTS_REPO" => _eventsRepo,
                "KBO_SILVER" => _silverPath,
                _ => null,
            };
        }
        return RebuildCommand.Run(args, _output, _error, name => Environment(name), _workspace);
    }

    [Fact]
    public void Rebuild_CreatesSilverAndReportsCounts()
    {
        int exitCode = Run();

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(_silverPath));
        Assert.Contains("1 event", _output.ToString(), StringComparison.Ordinal);
        Assert.Contains("1 session", _output.ToString(), StringComparison.Ordinal);
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
            ["--events-repo", Path.Combine(_workspace, "nope")],
            _output, _error, _ => _silverPath, _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("nope", _error.ToString(), StringComparison.Ordinal);
    }
}
