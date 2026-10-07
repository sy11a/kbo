using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Jobs;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Tests;

public sealed class IngestGraphMetricsJobTests : IDisposable
{
    private static readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-08-28T18:00:00Z", CultureInfo.InvariantCulture);

    private readonly string _workspace;
    private readonly string _eventsRepo;
    private readonly string _artifact;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public IngestGraphMetricsJobTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-ingest-tests").FullName;
        _eventsRepo = Path.Combine(_workspace, "kb-events");
        _artifact = Path.Combine(_workspace, "graph-metrics.ndjson");
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private KnowledgeRegistry RegistryWithPointer()
    {
        return KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: knowledge
                layer: global
                root: /home/admin/Knowledge
                metricsArtifact: {_artifact}
            """);
    }

    private IngestGraphMetricsJob Job(KnowledgeRegistry registry) => new(registry, _eventsRepo, new FixedTimeProvider(_now), CryptographicUlidEntropy.Instance);

    private static string SnapshotLine(
        string date = "2026-08-28",
        string source = "knowledge",
        string orphans = "10")
    {
        string line = $$"""
            {"origin":"job","date":"{{date}}","source":"{{source}}","notes":100,"orphans":{{orphans}},"links":200,"linkrot":5,"indegree":{"0":10,"1":40},"new_links_7d":3,"contract_version":1}
            """;
        return line;
    }

    private List<JsonObject> BronzeGraphMetrics() => [.. ReadAllBronze().Where(envelopeEvent => (string?)envelopeEvent[EnvelopeFields.Type] == EventTypes.GraphMetrics)];

    private List<JsonObject> ReadAllBronze()
    {
        List<JsonObject> events = [];
        string bronzeRoot = Path.Combine(_eventsRepo, "bronze");
        if (!Directory.Exists(bronzeRoot))
        {
            return events;
        }
        foreach (string monthFile in Directory.EnumerateFiles(bronzeRoot, "*.ndjsonl", SearchOption.AllDirectories))
        {
            foreach (string line in File.ReadLines(monthFile))
            {
                if (JsonNode.Parse(line) is JsonObject envelopeEvent)
                {
                    events.Add(envelopeEvent);
                }
            }
        }
        return events;
    }

    [Fact]
    public void Job_IsDailyUnderPulse()
    {
        // per R-003 — dead-man coverage rides the standard pulse mechanism.
        IngestGraphMetricsJob job = Job(RegistryWithPointer());
        Assert.Equal("ingest-graph-metrics", job.Name);
        Assert.Equal(JobCadence.Daily, job.Cadence);
    }

    [Fact]
    public void Run_NoSourceWithPointer_CompletesQuietly()
    {
        // per R-004 — no pointer configured: nothing to ingest, no error.
        KnowledgeRegistry registry = KnowledgeRegistry.Parse("""
            machine: test-machine
            sources:
              - id: knowledge
                layer: global
                root: /home/admin/Knowledge
            """);

        string summary = Job(registry).Run();

        Assert.Contains("no source carries metricsArtifact", summary, StringComparison.Ordinal);
        Assert.Empty(BronzeGraphMetrics());
    }

    [Fact]
    public void Run_AbsentArtifact_SkipsQuietly()
    {
        // per R-004 — the consumer ships before kbl's first emit: absent file
        // is a skip, not a failure.
        string summary = Job(RegistryWithPointer()).Run();

        Assert.Contains("artifact absent: knowledge", summary, StringComparison.Ordinal);
        Assert.Empty(BronzeGraphMetrics());
    }

    [Fact]
    public void Run_ValidArtifact_AppendsOneEnvelopePerSnapshot()
    {
        // per R-005 — data payload in, kbo-built envelope out.
        File.WriteAllText(_artifact, SnapshotLine() + "\n");

        string summary = Job(RegistryWithPointer()).Run();

        Assert.Contains("ingested 1", summary, StringComparison.Ordinal);
        List<JsonObject> events = BronzeGraphMetrics();
        JsonObject envelope = Assert.Single(events);
        Assert.Equal("graph.metrics/1", (string?)envelope[EnvelopeFields.SchemaRef]);
        Assert.Equal("kbo", (string?)envelope[EnvelopeFields.Agent]);
        Assert.Equal("knowledge", (string?)envelope[EnvelopeFields.Subject]);
        Assert.Equal("knowledge", (string?)envelope[EnvelopeFields.Kbroot]);
    }

    [Fact]
    public void Run_ReIngest_SkipsDuplicatesWithZeroNewSummary()
    {
        // per R-007 + R-008 — one bronze line per snapshot, ever; the tile
        // can never double-count.
        File.WriteAllText(_artifact, SnapshotLine() + "\n");
        _ = Job(RegistryWithPointer()).Run();
        _ = Assert.Single(BronzeGraphMetrics());

        string summary = Job(RegistryWithPointer()).Run();

        Assert.Contains("ingested 0", summary, StringComparison.Ordinal);
        Assert.Contains("skipped 1 duplicate", summary, StringComparison.Ordinal);
        _ = Assert.Single(BronzeGraphMetrics());
    }

    [Fact]
    public void Run_InvalidMetric_ThrowsAndAppendsNothing()
    {
        // per R-006 — schema violation is loud and all-or-nothing.
        File.WriteAllText(_artifact, SnapshotLine() + "\n" + SnapshotLine(date: "2026-08-27", orphans: "-3") + "\n");

        _ = Assert.Throws<InvalidOperationException>(() => Job(RegistryWithPointer()).Run());
        Assert.Empty(BronzeGraphMetrics());
    }

    [Fact]
    public void Run_ForeignSource_ThrowsAndAppendsNothing()
    {
        // per R-005 — pointer skew (payload names a foreign source) is loud.
        File.WriteAllText(_artifact, SnapshotLine(source: "someone-else") + "\n");

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => Job(RegistryWithPointer()).Run());
        Assert.Contains("does not match the registry row", exception.Message, StringComparison.Ordinal);
        Assert.Empty(BronzeGraphMetrics());
    }

    [Fact]
    public void Run_NotJsonLine_ThrowsAndAppendsNothing()
    {
        // per R-006 — an unparseable line fails the run before any append.
        File.WriteAllText(_artifact, SnapshotLine() + "\nnot json at all\n");

        _ = Assert.Throws<InvalidOperationException>(() => Job(RegistryWithPointer()).Run());
        Assert.Empty(BronzeGraphMetrics());
    }

    [Fact]
    public void Run_UnderPulseRunner_WrappedInJobCompleted()
    {
        // per R-003 — the dead-man sees a live job even in the absent state.
        using StringWriter output = new();
        int failures = PulseRunner.Run(
            new[] { Job(RegistryWithPointer()) }, _eventsRepo, "test-machine",
            new FixedTimeProvider(_now), CryptographicUlidEntropy.Instance, output);

        Assert.Equal(0, failures);
        Assert.Contains("ingest-graph-metrics: completed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(ReadAllBronze(), envelopeEvent =>
            (string?)envelopeEvent[EnvelopeFields.Type] == EventTypes.JobCompleted
            && (string?)envelopeEvent[EnvelopeFields.Subject] is "ingest-graph-metrics");
    }
}
