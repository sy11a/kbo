using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Jobs;
using Kbo.Schemas;

namespace Kbo.Tests;

public sealed class PulseRunnerTests : IDisposable
{
    private static readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-08-12T18:00:00Z", CultureInfo.InvariantCulture);
    private static readonly string[] _expectedOrder = ["harvest", "rebuild"];

    private readonly string _workspace;
    private readonly string _eventsRepo;
    private readonly StringWriter _output = new();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeJob(string name, JobCadence cadence, Action? action = null) : IPulseJob
    {
        public string Name => name;
        public JobCadence Cadence => cadence;
        public List<DateTimeOffset> Runs { get; } = [];

        public string Run()
        {
            Runs.Add(_now);
            action?.Invoke();
            return "ok";
        }
    }

    public PulseRunnerTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-pulse-tests").FullName;
        _eventsRepo = Path.Combine(_workspace, "kb-events");
    }

    public void Dispose()
    {
        _output.Dispose();
        Directory.Delete(_workspace, recursive: true);
    }

    private int RunPulse(params IPulseJob[] jobs) => PulseRunner.Run(jobs, _eventsRepo, "test-machine", new FixedTimeProvider(_now), CryptographicUlidEntropy.Instance, _output);

    private List<JsonObject> BronzeEvents()
    {
        string directory = Path.Combine(_eventsRepo, "bronze", "test-machine", "kbo");
        if (!Directory.Exists(directory))
        {
            return [];
        }
        return [.. Directory.EnumerateFiles(directory)
            .SelectMany(path => File.ReadLines(path))
            .Select(line => (JsonObject)JsonNode.Parse(line)!),
        ];
    }

    [Fact]
    public void DailyJobs_RunInOrder_AndEmitValidCompletedEvents()
    {
        List<string> order = [];
        FakeJob first = new("harvest", JobCadence.Daily, () => order.Add("harvest"));
        FakeJob second = new("rebuild", JobCadence.Daily, () => order.Add("rebuild"));

        int failures = RunPulse(first, second);

        Assert.Equal(0, failures);
        Assert.Equal(_expectedOrder, order, StringComparer.Ordinal);

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
        _ = Assert.Single(after.Runs);

        JsonObject failed = BronzeEvents().Single(e => (string?)e["type"] is "job.failed");
        Assert.Equal("archive", (string?)failed["subject"]);
        Assert.Contains("zstd not found", (string?)failed["data"]!["error"], StringComparison.Ordinal);
        Assert.True(new EventValidator().Validate(failed.ToJsonString()).IsValid);
    }

    [Fact]
    public void DailyJob_CompletedEarlierToday_IsSkipped()
    {
        new BronzeStore(_eventsRepo).Append(new[]
        {
            EventEnvelope.Create(
                "job.completed", "harvest", kbroot: null,
                new JsonObject { ["job"] = "harvest", ["duration_ms"] = 5 },
                "test-machine", "kbo", session: null, repo: null, task: null, model: null,
                _now.AddHours(-3), CryptographicUlidEntropy.Instance),
        });
        FakeJob harvest = new("harvest", JobCadence.Daily);

        int failures = RunPulse(harvest);

        Assert.Equal(0, failures);
        Assert.Empty(harvest.Runs);
    }

    [Fact]
    public void DailyJob_CompletedYesterday_RunsAgain()
    {
        new BronzeStore(_eventsRepo).Append(new[]
        {
            EventEnvelope.Create(
                "job.completed", "harvest", kbroot: null,
                new JsonObject { ["job"] = "harvest", ["duration_ms"] = 5 },
                "test-machine", "kbo", session: null, repo: null, task: null, model: null,
                _now.AddHours(-20), CryptographicUlidEntropy.Instance),
        });
        FakeJob harvest = new("harvest", JobCadence.Daily);

        _ = RunPulse(harvest);

        _ = Assert.Single(harvest.Runs);
    }

    [Fact]
    public void DailyJob_OnlyFailedToday_RetriesOnNextTick()
    {
        new BronzeStore(_eventsRepo).Append(new[]
        {
            EventEnvelope.Create(
                "job.failed", "backup", kbroot: null,
                new JsonObject { ["job"] = "backup", ["duration_ms"] = null, ["error"] = "locked" },
                "test-machine", "kbo", session: null, repo: null, task: null, model: null,
                _now.AddHours(-1), CryptographicUlidEntropy.Instance),
        });
        FakeJob backup = new("backup", JobCadence.Daily);

        _ = RunPulse(backup);

        _ = Assert.Single(backup.Runs);
    }

    [Fact]
    public void WeeklyJob_RecentlyCompleted_IsSkipped()
    {
        new BronzeStore(_eventsRepo).Append(new[]
        {
            EventEnvelope.Create(
                "job.completed", "report", kbroot: null,
                new JsonObject { ["job"] = "report", ["duration_ms"] = 5 },
                "test-machine", "kbo", session: null, repo: null, task: null, model: null,
                _now.AddDays(-2), CryptographicUlidEntropy.Instance),
        });
        FakeJob report = new("report", JobCadence.Weekly);

        int failures = RunPulse(report);

        Assert.Equal(0, failures);
        Assert.Empty(report.Runs);
    }

    [Fact]
    public void WeeklyJob_StaleOrNeverCompleted_Runs()
    {
        new BronzeStore(_eventsRepo).Append(new[]
        {
            EventEnvelope.Create(
                "job.completed", "report", kbroot: null,
                new JsonObject { ["job"] = "report", ["duration_ms"] = 5 },
                "test-machine", "kbo", session: null, repo: null, task: null, model: null,
                _now.AddDays(-8), CryptographicUlidEntropy.Instance),
        });
        FakeJob report = new("report", JobCadence.Weekly);
        FakeJob fresh = new("never-ran", JobCadence.Weekly);

        int failures = RunPulse(report, fresh);

        Assert.Equal(0, failures);
        _ = Assert.Single(report.Runs);
        _ = Assert.Single(fresh.Runs);
    }
}
