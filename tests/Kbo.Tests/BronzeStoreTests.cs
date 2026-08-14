using System.Text.Json.Nodes;
using Kbo.Bronze;

namespace Kbo.Tests;

public class BronzeStoreTests : IDisposable
{
    private readonly string eventsRepo;

    public BronzeStoreTests()
    {
        eventsRepo = Path.Combine(Directory.CreateTempSubdirectory("kbo-bronze-tests").FullName, "kb-events");
    }

    public void Dispose()
    {
        Directory.Delete(Path.GetDirectoryName(eventsRepo)!, recursive: true);
    }

    private static JsonObject Event(string time, string? origin = null, string? transcript = null)
    {
        return new JsonObject
        {
            ["id"] = "01J2ZK8Q000000000000000901",
            ["type"] = "knowledge.read",
            ["time"] = time,
            ["machine"] = "test-machine",
            ["agent"] = "claude-code",
            ["data"] = new JsonObject { ["origin"] = origin, ["transcript"] = transcript },
        };
    }

    [Fact]
    public void Append_CreatesRepoWithGitAndMonthFile()
    {
        BronzeStore store = new(eventsRepo);
        store.Append(new[] { Event("2026-08-11T15:00:00Z") });

        string monthFile = Path.Combine(eventsRepo, "bronze", "test-machine", "claude-code", "2026-08.ndjsonl");
        Assert.True(File.Exists(monthFile));
        Assert.True(Directory.Exists(Path.Combine(eventsRepo, ".git")));

        string[] lines = File.ReadAllLines(monthFile);
        Assert.Single(lines);
        Assert.Contains("\"01J2ZK8Q000000000000000901\"", lines[0]);
    }

    [Fact]
    public void HarvestedTranscripts_ReturnsOnlyTranscriptsWithHarvestOriginEvents()
    {
        BronzeStore store = new(eventsRepo);
        store.Append(new[]
        {
            Event("2026-08-11T15:00:00Z", origin: "hook"),
            Event("2026-07-01T10:00:00Z", origin: "harvest", transcript: "file-a"),
            Event("2026-07-02T10:00:00Z", origin: "harvest", transcript: "file-b"),
            Event("2026-07-03T10:00:00Z", origin: "harvest", transcript: "file-b"),
        });

        IReadOnlySet<string> harvested = store.HarvestedTranscripts();

        Assert.Equal(new HashSet<string> { "file-a", "file-b" }, harvested);
    }

    [Fact]
    public void HarvestedTranscripts_EmptyOrMissingRepo_ReturnsEmpty()
    {
        Assert.Empty(new BronzeStore(eventsRepo).HarvestedTranscripts());
    }

    [Fact]
    public void SeenTranscripts_CoversHarvestStampsAndHookTranscriptPaths()
    {
        BronzeStore store = new(eventsRepo);
        JsonObject hookEvent = Event("2026-08-11T15:00:00Z", origin: "hook");
        hookEvent["data"]!["raw"] = new JsonObject
        {
            ["transcript_path"] = "/home/u/.claude/projects/-home-u-repo/hook-session-file.jsonl",
        };
        store.Append(new[]
        {
            hookEvent,
            Event("2026-07-01T10:00:00Z", origin: "harvest", transcript: "harvested-file"),
        });

        IReadOnlySet<string> seen = store.SeenTranscripts();

        Assert.Equal(new HashSet<string> { "hook-session-file", "harvested-file" }, seen);
    }

    [Fact]
    public void Append_TwiceAndAcrossMonths_AppendsAndBuckets()
    {
        BronzeStore store = new(eventsRepo);
        store.Append(new[] { Event("2026-08-11T15:00:00Z"), Event("2026-08-11T15:00:01Z") });
        store.Append(new[] { Event("2026-09-01T00:00:00Z") });

        string august = Path.Combine(eventsRepo, "bronze", "test-machine", "claude-code", "2026-08.ndjsonl");
        string september = Path.Combine(eventsRepo, "bronze", "test-machine", "claude-code", "2026-09.ndjsonl");
        Assert.Equal(2, File.ReadAllLines(august).Length);
        Assert.Single(File.ReadAllLines(september));
    }
}
