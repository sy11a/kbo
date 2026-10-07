using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Cli;
using Kbo.Jobs;
using Kbo.Schemas;

namespace Kbo.Tests;

public sealed class DoctorCommandTests : IDisposable
{
    private static readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-08-12T18:00:00Z", CultureInfo.InvariantCulture);

    private readonly string _workspace;
    private readonly string _eventsRepo;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeRunner(string timerState = "active") : IProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Invocations { get; } = [];

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Invocations.Add((fileName, arguments));
            if (fileName is "systemctl")
            {
                return new ProcessResult(timerState is "active" ? 0 : 3, timerState + "\n", string.Empty);
            }
            return new ProcessResult(0, string.Empty, string.Empty);
        }
    }

    public DoctorCommandTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-doctor-tests").FullName;
        _eventsRepo = Path.Combine(_workspace, "kb-events");
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_workspace, recursive: true);
    }

    private void JobCompleted(string job, double daysAgo)
    {
        new BronzeStore(_eventsRepo).Append(new[]
        {
            EventEnvelope.Create(
                "job.completed", job, kbroot: null,
                new JsonObject { ["job"] = job, ["duration_ms"] = 5 },
                "test-machine", "kbo", session: null, repo: null, task: null, model: null,
                _now.AddDays(-daysAgo), CryptographicUlidEntropy.Instance),
        });
    }

    private int Run(FakeRunner runner, params string[] args)
    {
        string? Environment(string name) => name is "KBO_EVENTS_REPO" ? _eventsRepo : null;
        return DoctorCommand.Run(args, _output, _error, variable => Environment(variable), _workspace, runner, new FixedTimeProvider(_now));
    }

    [Fact]
    public void Healthy_TimerActiveAndJobsFresh_Exit0()
    {
        JobCompleted("harvest", 0.2);
        FakeRunner runner = new();

        int exitCode = Run(runner);

        Assert.Equal(0, exitCode);
        Assert.Contains("timer: active", _output.ToString(), StringComparison.Ordinal);
        Assert.Contains("all jobs healthy", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WeeklyJobsWithinTheirCadence_AreNotSilent()
    {
        JobCompleted("harvest", 0.2);
        JobCompleted("audit", 4.0);
        JobCompleted("report", 5.0);
        FakeRunner runner = new();

        int exitCode = Run(runner);

        Assert.Equal(0, exitCode);
        Assert.Contains("audit: ok", _output.ToString(), StringComparison.Ordinal);
        Assert.Contains("report: ok", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WeeklyJobPastItsThreshold_IsSilent()
    {
        JobCompleted("harvest", 0.2);
        JobCompleted("audit", 10.0);
        FakeRunner runner = new();

        int exitCode = Run(runner);

        Assert.Equal(1, exitCode);
        Assert.Contains("audit: SILENT", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SilentJob_Exit1_AndReportsIt()
    {
        JobCompleted("harvest", 0.2);
        JobCompleted("backup", 5.0);
        FakeRunner runner = new();

        int exitCode = Run(runner);

        Assert.Equal(1, exitCode);
        Assert.Contains("backup", _output.ToString(), StringComparison.Ordinal);
        Assert.Contains("5", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DeadTimer_Exit1()
    {
        JobCompleted("harvest", 0.2);
        FakeRunner runner = new(timerState: "inactive");

        int exitCode = Run(runner);

        Assert.Equal(1, exitCode);
        Assert.Contains("timer: inactive", _output.ToString(), StringComparison.Ordinal);
    }

    private void WriteCaptureError(DateTimeOffset when)
    {
        string logPath = Path.Combine(_workspace, ".local", "state", "kbo", "capture-errors.log");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllText(logPath,
            string.Create(CultureInfo.InvariantCulture, $"{when:yyyy-MM-dd'T'HH:mm:ss'Z'}\tclaude-code\tregistry: not found\n"));
    }

    [Fact]
    public void RecentCaptureDrops_ReportedAndExit1()
    {
        JobCompleted("harvest", 0.2);
        WriteCaptureError(_now.AddHours(-2));
        FakeRunner runner = new();

        int exitCode = Run(runner);

        Assert.Equal(1, exitCode);
        Assert.Contains("capture errors: 1", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StaleCaptureDrops_ReportedButNotAProblem()
    {
        JobCompleted("harvest", 0.2);
        WriteCaptureError(_now.AddDays(-30));
        FakeRunner runner = new();

        int exitCode = Run(runner);

        Assert.Equal(0, exitCode);
        Assert.Contains("capture errors: 1", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Notify_SendsCriticalOnProblem_NormalWhenHealthy()
    {
        JobCompleted("backup", 5.0);
        FakeRunner problemRunner = new();
        _ = Run(problemRunner, "--notify");
        Assert.Contains(problemRunner.Invocations,
            i => i.FileName is "notify-send" && i.Arguments.Contains("critical", StringComparer.Ordinal));

        JobCompleted("backup", 0.1);
        FakeRunner healthyRunner = new();
        _ = Run(healthyRunner, "--notify");
        (string FileName, IReadOnlyList<string> Arguments) =
            healthyRunner.Invocations.Single(i => i.FileName is "notify-send");
        Assert.DoesNotContain("critical", Arguments, StringComparer.Ordinal);
    }
}
