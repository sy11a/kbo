using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Cli;

namespace Kbo.Tests;

public sealed class PulseCommandTests : IDisposable
{
    private const string Machine = "test-machine";

    private static readonly string[] _allJobs =
    [
        "harvest", "harvest-opencode", "rebuild", "ingest-graph-metrics", "archive",
        "vault-git", "bronze-git", "backup", "report", "audit",
    ];

    private readonly string _workspace;
    private readonly string _vaultRoot;
    private readonly string _eventsRepo;
    private readonly string _registryPath;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public PulseCommandTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-pulse-command-tests").FullName;
        _vaultRoot = Path.Combine(_workspace, "Knowledge");
        _eventsRepo = Path.Combine(_workspace, "kb-events");
        _registryPath = Path.Combine(_workspace, "registry.yaml");
        _ = Directory.CreateDirectory(_vaultRoot);
        File.WriteAllText(_registryPath, $"""
            machine: {Machine}
            sources:
              - id: vault
                layer: global
                root: {_vaultRoot}
            """);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_workspace, recursive: true);
    }

    private string? Environment(string name)
    {
        return name switch
        {
            "KBO_REGISTRY" => _registryPath,
            "KBO_EVENTS_REPO" => _eventsRepo,
            "KBO_SILVER" => Path.Combine(_workspace, "silver.duckdb"),
            "KB_ARCHIVE_ROOT" => Path.Combine(_workspace, "Archive"),
            "KB_RESTIC_REPO" => Path.Combine(_workspace, "restic"),
            _ => null,
        };
    }

    private int Run(params string[] args) => PulseCommand.Run(args, _output, _error, name => Environment(name), _workspace);

    private void SeedCompleted(IEnumerable<string> jobs)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        new BronzeStore(_eventsRepo).Append(jobs.Select(job => EventEnvelope.Create(
            "job.completed", job, kbroot: null,
            new JsonObject { ["job"] = job, ["duration_ms"] = 1 },
            Machine, "kbo", session: null, repo: null, task: null, model: null,
            now, CryptographicUlidEntropy.Instance)).ToArray());
    }

    [Fact]
    public void Pulse_WithArguments_PrintsUsageAndFails()
    {
        int exitCode = Run("extra");

        Assert.Equal(1, exitCode);
        Assert.Contains("usage: kbo pulse", _error.ToString(), StringComparison.Ordinal);
        Assert.Empty(_output.ToString());
    }

    [Fact]
    public void Pulse_RegistryMissing_ReportsErrorAndFails()
    {
        File.Delete(_registryPath);

        int exitCode = Run();

        Assert.Equal(1, exitCode);
        Assert.Contains("registry file not found", _error.ToString(), StringComparison.Ordinal);
        Assert.Empty(_output.ToString());
    }

    [Fact]
    public void Pulse_EveryJobCompletedToday_SkipsAllAndSucceeds()
    {
        SeedCompleted(_allJobs);

        int exitCode = Run();

        Assert.Equal(0, exitCode);
        string output = _output.ToString();
        foreach (string job in _allJobs)
        {
            Assert.Contains($"{job}: not due", output, StringComparison.Ordinal);
        }
        Assert.Empty(_error.ToString());
    }

    [Fact]
    public void Pulse_FailingJob_ReturnsFailure()
    {
        // The restic password file under the workspace home never exists, so the
        // backup job fails whether or not restic is installed.
        SeedCompleted(_allJobs.Where(job => job is not "backup"));

        int exitCode = Run();

        Assert.Equal(1, exitCode);
        Assert.Contains("backup: FAILED", _output.ToString(), StringComparison.Ordinal);
    }
}
