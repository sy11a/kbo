using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Cli;
using Kbo.Jobs;
using Kbo.Schemas;

namespace Kbo.Tests;

public class DoctorCommandTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-12T18:00:00Z", CultureInfo.InvariantCulture);

    private readonly string workspace;
    private readonly string eventsRepo;
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeRunner(string timerState = "active") : IProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Invocations { get; } = new();

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Invocations.Add((fileName, arguments));
            if (fileName == "systemctl")
            {
                return new ProcessResult(timerState == "active" ? 0 : 3, timerState + "\n", "");
            }
            return new ProcessResult(0, "", "");
        }
    }

    public DoctorCommandTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-doctor-tests").FullName;
        eventsRepo = Path.Combine(workspace, "kb-events");
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private void JobCompleted(string job, double daysAgo)
    {
        new BronzeStore(eventsRepo).Append(new[]
        {
            EventEnvelope.Create(
                "job.completed", job, null,
                new JsonObject { ["job"] = job, ["duration_ms"] = 5 },
                "test-machine", "kbo", null, null, null, null,
                Now.AddDays(-daysAgo), new Random(1)),
        });
    }

    private int Run(FakeRunner runner, params string[] args)
    {
        string? Environment(string name) => name == "KBO_EVENTS_REPO" ? eventsRepo : null;
        return DoctorCommand.Run(args, output, error, Environment, workspace, runner, new FixedTimeProvider(Now));
    }

    [Fact]
    public void Healthy_TimerActiveAndJobsFresh_Exit0()
    {
        JobCompleted("harvest", 0.2);
        FakeRunner runner = new();

        int exitCode = Run(runner);

        Assert.Equal(0, exitCode);
        Assert.Contains("timer: active", output.ToString());
        Assert.Contains("all jobs healthy", output.ToString());
    }

    [Fact]
    public void SilentJob_Exit1_AndReportsIt()
    {
        JobCompleted("harvest", 0.2);
        JobCompleted("backup", 5.0);
        FakeRunner runner = new();

        int exitCode = Run(runner);

        Assert.Equal(1, exitCode);
        Assert.Contains("backup", output.ToString());
        Assert.Contains("5", output.ToString());
    }

    [Fact]
    public void DeadTimer_Exit1()
    {
        JobCompleted("harvest", 0.2);
        FakeRunner runner = new(timerState: "inactive");

        int exitCode = Run(runner);

        Assert.Equal(1, exitCode);
        Assert.Contains("timer: inactive", output.ToString());
    }

    [Fact]
    public void Notify_SendsCriticalOnProblem_NormalWhenHealthy()
    {
        JobCompleted("backup", 5.0);
        FakeRunner problemRunner = new();
        Run(problemRunner, "--notify");
        Assert.Contains(problemRunner.Invocations,
            i => i.FileName == "notify-send" && i.Arguments.Contains("critical"));

        JobCompleted("backup", 0.1);
        FakeRunner healthyRunner = new();
        Run(healthyRunner, "--notify");
        (string FileName, IReadOnlyList<string> Arguments) notify =
            healthyRunner.Invocations.Single(i => i.FileName == "notify-send");
        Assert.DoesNotContain("critical", notify.Arguments);
    }
}
