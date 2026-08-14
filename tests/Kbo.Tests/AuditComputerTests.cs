using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Gold;
using Kbo.Jobs;
using Kbo.Silver;

namespace Kbo.Tests;

public class AuditComputerTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-12T20:00:00Z", CultureInfo.InvariantCulture);

    private readonly string workspace;
    private readonly string transcriptsRoot;
    private readonly string eventsRepo;
    private readonly string silverPath;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public AuditComputerTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-audit-tests").FullName;
        transcriptsRoot = Path.Combine(workspace, "projects");
        eventsRepo = Path.Combine(workspace, "kb-events");
        silverPath = Path.Combine(workspace, "silver.duckdb");
        Directory.CreateDirectory(Path.Combine(transcriptsRoot, "proj-a"));
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private void Transcript(string stem, int mtimeDaysAgo)
    {
        string path = Path.Combine(transcriptsRoot, "proj-a", stem + ".jsonl");
        File.WriteAllText(path, "{}\n");
        File.SetLastWriteTimeUtc(path, Now.AddDays(-mtimeDaysAgo).UtcDateTime);
    }

    private static JsonObject Event(string id, string type, string? kbroot, string subject, string? origin = "harvest", string? transcript = null)
    {
        JsonObject data = new() { ["origin"] = origin };
        if (transcript is not null)
        {
            data["transcript"] = transcript;
        }
        return new JsonObject
        {
            ["id"] = id,
            ["type"] = type,
            ["time"] = "2026-08-01T10:00:00Z",
            ["subject"] = subject,
            ["machine"] = "test-machine",
            ["agent"] = "claude-code",
            ["session"] = "sess-1",
            ["kbroot"] = kbroot,
            ["data"] = data,
        };
    }

    private AuditReport Compute(params JsonObject[] events)
    {
        if (events.Length > 0)
        {
            new BronzeStore(eventsRepo).Append(events);
        }
        SilverRebuilder.Rebuild(eventsRepo, silverPath);
        RetentionManifest manifest = new(
            "claude-code",
            Array.Empty<ArchiveEntry>(),
            new FileTreeEntry(transcriptsRoot, "*.jsonl", "claude-code/projects"));
        Kbo.Registry.KnowledgeRegistry registry = Kbo.Registry.KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: now-registered
                layer: local
                root: {Path.Combine(workspace, "now-registered")}
            """);
        return AuditComputer.Compute(
            new[] { manifest }, eventsRepo, silverPath, registry, new FixedTimeProvider(Now));
    }

    [Fact]
    public void MissingSessions_FlagsTranscriptsBronzeNeverSaw_WithSinceDate()
    {
        Transcript("seen-file", mtimeDaysAgo: 10);
        Transcript("missing-old", mtimeDaysAgo: 9);
        Transcript("missing-new", mtimeDaysAgo: 2);

        AuditReport report = Compute(
            Event("01D00000000000000000000001", "knowledge.read", null, "/x.md", transcript: "seen-file"));

        MissingSessionsFinding finding = Assert.Single(report.MissingSessions);
        Assert.Equal("claude-code", finding.Agent);
        Assert.Equal(2, finding.Count);
        Assert.Equal(Now.AddDays(-9).UtcDateTime.Date, finding.MissingSince.UtcDateTime.Date);
        Assert.Contains("missing-old", finding.Transcripts);
        Assert.Contains("missing-new", finding.Transcripts);
    }

    [Fact]
    public void MissingSessions_AllSeen_NoFinding()
    {
        Transcript("seen-file", mtimeDaysAgo: 3);

        AuditReport report = Compute(
            Event("01D00000000000000000000002", "knowledge.read", null, "/x.md", transcript: "seen-file"));

        Assert.Empty(report.MissingSessions);
    }

    [Fact]
    public void UnregisteredKnowledge_ExcludesDirsNowCoveredByARegisteredRoot()
    {
        string registeredDirectory = Path.Combine(workspace, "now-registered");
        Directory.CreateDirectory(registeredDirectory);
        AuditReport report = Compute(
            Event("01D00000000000000000000008", "knowledge.read", null, Path.Combine(registeredDirectory, "old.md"), transcript: "t9"),
            Event("01D00000000000000000000009", "knowledge.read", null, "/still/unregistered/x.md", transcript: "t9"));

        Assert.DoesNotContain(report.UnregisteredSources, f => f.Directory == registeredDirectory);
        Assert.Contains(report.UnregisteredSources, f => f.Directory == "/still/unregistered");
    }

    [Fact]
    public void UnregisteredKnowledge_GroupsNullKbrootMarkdownReadsByDirectory()
    {
        AuditReport report = Compute(
            Event("01D00000000000000000000003", "knowledge.read", null, "/home/u/Notes/a.md", transcript: "t1"),
            Event("01D00000000000000000000004", "knowledge.read", null, "/home/u/Notes/b.md", transcript: "t1"),
            Event("01D00000000000000000000005", "knowledge.read", null, "/home/u/code/README.md", transcript: "t1"),
            Event("01D00000000000000000000006", "knowledge.read", "vault", "/home/u/Knowledge/c.md", transcript: "t1"),
            Event("01D00000000000000000000007", "knowledge.read", null, "/home/u/code/Program.cs", transcript: "t1"));

        UnregisteredSourceFinding top = report.UnregisteredSources[0];
        Assert.Equal("/home/u/Notes", top.Directory);
        Assert.Equal(2, top.ReadCount);
        Assert.Contains(report.UnregisteredSources, f => f.Directory == "/home/u/code" && f.ReadCount == 1);
        Assert.DoesNotContain(report.UnregisteredSources, f => f.Directory == "/home/u/Knowledge");
    }
}
