using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Jobs;
using Kbo.Schemas;

namespace Kbo.Tests;

public class PulseRunnerTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-12T18:00:00Z", CultureInfo.InvariantCulture);

    private readonly string workspace;
    private readonly string eventsRepo;
    private readonly StringWriter output = new();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeJob(string name, JobCadence cadence, Action? action = null) : IPulseJob
    {
        public string Name => name;
        public JobCadence Cadence => cadence;
        public List<DateTimeOffset> Runs { get; } = new();

        public string Run()
        {
            Runs.Add(Now);
            action?.Invoke();
            return "ok";
        }
    }

    public PulseRunnerTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-pulse-tests").FullName;
        eventsRepo = Path.Combine(workspace, "kb-events");
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private int RunPulse(params IPulseJob[] jobs)
    {
        return PulseRunner.Run(jobs, eventsRepo, "test-machine", new FixedTimeProvider(Now), new Random(42), output);
    }

    private List<JsonObject> BronzeEvents()
    {
        string directory = Path.Combine(eventsRepo, "bronze", "test-machine", "kbo");
        if (!Directory.Exists(directory))
        {
            return new List<JsonObject>();
        }
        return Directory.EnumerateFiles(directory)
            .SelectMany(File.ReadLines)
            .Select(line => (JsonObject)JsonNode.Parse(line)!)
            .ToList();
    }

    [Fact]
    public void DailyJobs_RunInOrder_AndEmitValidCompletedEvents()
    {
        List<string> order = new();
        FakeJob first = new("harvest", JobCadence.Daily, () => order.Add("harvest"));
        FakeJob second = new("rebuild", JobCadence.Daily, () => order.Add("rebuild"));

        int failures = RunPulse(first, second);

        Assert.Equal(0, failures);
        Assert.Equal(new[] { "harvest", "rebuild" }, order);

        List<JsonObject> events = BronzeEvents();
        Assert.Equal(2, events.Count);
        EventValidator validator = new();
        foreach (JsonObject jobEvent in events)
        {
            EventValidationResult result = validator.Validate(jobEvent.ToJsonString());
            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Equal("job.completed", (string?)jobEvent["type"]);
            Assert.Equal("kbo", (string?)jobEvent["agent"]);
        }
    }

    [Fact]
    public void FailingJob_EmitsJobFailed_AndPulseContinues()
    {
        FakeJob failing = new("archive", JobCadence.Daily, () => throw new InvalidOperationException("zstd not found"));
        FakeJob after = new("backup", JobCadence.Daily);

        int failures = RunPulse(failing, after);

        Assert.Equal(1, failures);
        Assert.Single(after.Runs);

        JsonObject failed = BronzeEvents().Single(e => (string?)e["type"] == "job.failed");
        Assert.Equal("archive", (string?)failed["subject"]);
        Assert.Contains("zstd not found", (string?)failed["data"]!["error"]);
        Assert.True(new EventValidator().Validate(failed.ToJsonString()).IsValid);
    }

    [Fact]
    public void DailyJob_CompletedEarlierToday_IsSkipped()
    {
        new BronzeStore(eventsRepo).Append(new[]
        {
            EventEnvelope.Create(
                "job.completed", "harvest", null,
                new JsonObject { ["job"] = "harvest", ["duration_ms"] = 5 },
                "test-machine", "kbo", null, null, null, null,
                Now.AddHours(-3), new Random(1)),
        });
        FakeJob harvest = new("harvest", JobCadence.Daily);

        int failures = RunPulse(harvest);

        Assert.Equal(0, failures);
        Assert.Empty(harvest.Runs);
    }

    [Fact]
    public void DailyJob_CompletedYesterday_RunsAgain()
    {
        new BronzeStore(eventsRepo).Append(new[]
        {
            EventEnvelope.Create(
                "job.completed", "harvest", null,
                new JsonObject { ["job"] = "harvest", ["duration_ms"] = 5 },
                "test-machine", "kbo", null, null, null, null,
                Now.AddHours(-20), new Random(1)),
        });
        FakeJob harvest = new("harvest", JobCadence.Daily);

        RunPulse(harvest);

        Assert.Single(harvest.Runs);
    }

    [Fact]
    public void DailyJob_OnlyFailedToday_RetriesOnNextTick()
    {
        new BronzeStore(eventsRepo).Append(new[]
        {
            EventEnvelope.Create(
                "job.failed", "backup", null,
                new JsonObject { ["job"] = "backup", ["duration_ms"] = null, ["error"] = "locked" },
                "test-machine", "kbo", null, null, null, null,
                Now.AddHours(-1), new Random(1)),
        });
        FakeJob backup = new("backup", JobCadence.Daily);

        RunPulse(backup);

        Assert.Single(backup.Runs);
    }

    [Fact]
    public void WeeklyJob_RecentlyCompleted_IsSkipped()
    {
        new BronzeStore(eventsRepo).Append(new[]
        {
            EventEnvelope.Create(
                "job.completed", "report", null,
                new JsonObject { ["job"] = "report", ["duration_ms"] = 5 },
                "test-machine", "kbo", null, null, null, null,
                Now.AddDays(-2), new Random(1)),
        });
        FakeJob report = new("report", JobCadence.Weekly);

        int failures = RunPulse(report);

        Assert.Equal(0, failures);
        Assert.Empty(report.Runs);
    }

    [Fact]
    public void WeeklyJob_StaleOrNeverCompleted_Runs()
    {
        new BronzeStore(eventsRepo).Append(new[]
        {
            EventEnvelope.Create(
                "job.completed", "report", null,
                new JsonObject { ["job"] = "report", ["duration_ms"] = 5 },
                "test-machine", "kbo", null, null, null, null,
                Now.AddDays(-8), new Random(1)),
        });
        FakeJob report = new("report", JobCadence.Weekly);
        FakeJob fresh = new("never-ran", JobCadence.Weekly);

        int failures = RunPulse(report, fresh);

        Assert.Equal(0, failures);
        Assert.Single(report.Runs);
        Assert.Single(fresh.Runs);
    }
}
