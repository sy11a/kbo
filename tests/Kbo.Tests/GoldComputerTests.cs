using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Gold;
using Kbo.Registry;
using Kbo.Silver;

namespace Kbo.Tests;

public class GoldComputerTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-12T12:00:00Z", CultureInfo.InvariantCulture);

    private readonly string workspace;
    private readonly string vaultRoot;
    private readonly string skillsRoot;
    private readonly string silverPath;
    private readonly KnowledgeRegistry registry;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public GoldComputerTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-gold-tests").FullName;
        vaultRoot = Path.Combine(workspace, "Knowledge");
        skillsRoot = Path.Combine(workspace, "skills");
        silverPath = Path.Combine(workspace, "silver.duckdb");
        Directory.CreateDirectory(vaultRoot);
        Directory.CreateDirectory(skillsRoot);
        registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {vaultRoot}
              - id: skills
                layer: skills
                root: {skillsRoot}
            """);
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private string Note(string relativePath, int modifiedDaysAgo)
    {
        string path = Path.Combine(vaultRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "# note\n");
        File.SetLastWriteTimeUtc(path, Now.AddDays(-modifiedDaysAgo).UtcDateTime);
        return path;
    }

    private static JsonObject ReadEvent(string id, string path, int daysAgo)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["type"] = "knowledge.read",
            ["time"] = Now.AddDays(-daysAgo).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["subject"] = path,
            ["machine"] = "test-machine",
            ["agent"] = "claude-code",
            ["session"] = "sess-1",
            ["kbroot"] = "vault",
            ["data"] = new JsonObject { ["origin"] = "harvest", ["transcript"] = "t-1" },
        };
    }

    private static JsonObject WriteEvent(string id, string path, int daysAgo)
    {
        JsonObject written = ReadEvent(id, path, daysAgo);
        written["type"] = "knowledge.written";
        return written;
    }

    private GoldReport Compute(params JsonObject[] events)
    {
        string eventsRepo = Path.Combine(workspace, "kb-events");
        new BronzeStore(eventsRepo).Append(events);
        SilverRebuilder.Rebuild(eventsRepo, silverPath);
        return GoldComputer.Compute(silverPath, registry, new FixedTimeProvider(Now));
    }

    [Fact]
    public void DeadNotes_OldUnreadNoteIsDead_ReadOrYoungNotesAreNot()
    {
        string deadPath = Note("old-unread.md", modifiedDaysAgo: 200);
        Note("young-unread.md", modifiedDaysAgo: 5);
        string readPath = Note("old-read.md", modifiedDaysAgo: 200);

        GoldReport report = Compute(ReadEvent("01B00000000000000000000001", readPath, daysAgo: 10));

        List<string> deadPaths = report.DeadNotes.Select(n => n.Path).ToList();
        Assert.Contains(deadPath, deadPaths);
        Assert.DoesNotContain(readPath, deadPaths);
        Assert.DoesNotContain(Path.Combine(vaultRoot, "young-unread.md"), deadPaths);
    }

    [Fact]
    public void DeadNotes_OnlyOldReadsStillCountAsDead()
    {
        string path = Note("stale-read.md", modifiedDaysAgo: 200);
        string activePath = Note("active.md", modifiedDaysAgo: 5);

        GoldReport report = Compute(
            ReadEvent("01B00000000000000000000002", path, daysAgo: 100),
            ReadEvent("01B00000000000000000000010", activePath, daysAgo: 1));

        Kbo.Gold.DeadNote dead = Assert.Single(report.DeadNotes, n => n.Path == path);
        Assert.NotNull(dead.LastRead);
    }

    [Fact]
    public void HotNotes_CountsWindowAndTotalReads()
    {
        string path = Note("hot.md", modifiedDaysAgo: 10);

        GoldReport report = Compute(
            ReadEvent("01B00000000000000000000003", path, daysAgo: 1),
            ReadEvent("01B00000000000000000000004", path, daysAgo: 2),
            ReadEvent("01B00000000000000000000005", path, daysAgo: 100));

        Kbo.Gold.HotNote hot = Assert.Single(report.HotNotes, n => n.Path == path);
        Assert.Equal(2, hot.ReadsInWindow);
        Assert.Equal(3, hot.ReadsTotal);
    }

    [Fact]
    public void StaleNotes_RequireThreeWindowReadsAndOldMtime()
    {
        string stalePath = Note("stale.md", modifiedDaysAgo: 120);
        string freshPath = Note("fresh.md", modifiedDaysAgo: 10);

        GoldReport report = Compute(
            ReadEvent("01B00000000000000000000006", stalePath, daysAgo: 1),
            ReadEvent("01B00000000000000000000007", stalePath, daysAgo: 2),
            ReadEvent("01B00000000000000000000008", stalePath, daysAgo: 3),
            ReadEvent("01B00000000000000000000009", freshPath, daysAgo: 1),
            ReadEvent("01B0000000000000000000000A", freshPath, daysAgo: 2),
            ReadEvent("01B0000000000000000000000B", freshPath, daysAgo: 3));

        Assert.Single(report.StaleNotes, n => n.Path == stalePath);
        Assert.DoesNotContain(report.StaleNotes, n => n.Path == freshPath);
    }

    [Fact]
    public void DeadNotes_HistoricalKbrootNullReadsOfInventoryPaths_StillCount()
    {
        string latePath = Note("late-registered.md", modifiedDaysAgo: 200);
        JsonObject nullKbrootRead = ReadEvent("01B0000000000000000000000D", latePath, daysAgo: 5);
        nullKbrootRead["kbroot"] = null;

        GoldReport report = Compute(nullKbrootRead);

        Assert.DoesNotContain(report.DeadNotes, n => n.Path == latePath);
        Assert.Contains(report.HotNotes, n => n.Path == latePath);
    }

    [Fact]
    public void LifecycleNotes_AreCountedButNeverDead()
    {
        Note("Glossary/beacon.md", modifiedDaysAgo: 40);
        Note("docs/superpowers/plans/2026-06-01-old-plan.md", modifiedDaysAgo: 40);
        string activePath = Note("active.md", modifiedDaysAgo: 5);

        GoldReport report = Compute(ReadEvent("01B00000000000000000000011", activePath, daysAgo: 1));

        Assert.Single(report.DeadNotes);
        Assert.EndsWith("Glossary/beacon.md", report.DeadNotes[0].Path);
        Assert.Equal(1, report.LifecycleCounts["vault"]);
    }

    [Fact]
    public void DormantSources_RecentActivityKeepsDeadNotesOnWorklist()
    {
        string deadPath = Note("Glossary/unread.md", modifiedDaysAgo: 40);
        string readPath = Note("Now.md", modifiedDaysAgo: 5);

        GoldReport report = Compute(ReadEvent("01B0000000000000000000000E", readPath, daysAgo: 2));

        Assert.Contains(report.DeadNotes, note => note.Path == deadPath);
        Assert.DoesNotContain(report.DormantSources, source => source.SourceId == "vault");
    }

    [Fact]
    public void DormantSources_SilentSourceIsDormantAndDeadNotesWithheld()
    {
        string deadPath = Note("Glossary/unread.md", modifiedDaysAgo: 40);
        string oldReadPath = Note("Now.md", modifiedDaysAgo: 40);

        GoldReport report = Compute(ReadEvent("01B0000000000000000000000F", oldReadPath, daysAgo: 30));

        Assert.DoesNotContain(report.DeadNotes, note => note.Path == deadPath);
        DormantSource dormant = Assert.Single(report.DormantSources, source => source.SourceId == "vault");
        Assert.Equal(1, dormant.WithheldDeadNotes);
        Assert.NotNull(dormant.LastActivity);
    }

    [Fact]
    public void DormantSources_WriteEventsAloneAreNotActivity()
    {
        string deadPath = Note("Glossary/unread.md", modifiedDaysAgo: 40);
        string stampPath = Path.Combine(vaultRoot, "docs", "ai", "manifest.json");

        GoldReport report = Compute(WriteEvent("01B00000000000000000000012", stampPath, daysAgo: 5));

        Assert.DoesNotContain(report.DeadNotes, note => note.Path == deadPath);
        DormantSource dormant = Assert.Single(report.DormantSources, source => source.SourceId == "vault");
        Assert.Equal(1, dormant.WithheldDeadNotes);
        Assert.Null(dormant.LastActivity);
    }

    [Fact]
    public void Report_CarriesInventoryTotalsAndGeneratedAt()
    {
        Note("a.md", 200);
        Note("sub/b.md", 5);
        File.WriteAllText(Path.Combine(skillsRoot, "skill.md"), "s");
        File.WriteAllText(Path.Combine(vaultRoot, "not-a-note.canvas"), "x");

        GoldReport report = Compute(ReadEvent("01B0000000000000000000000C", Path.Combine(vaultRoot, "a.md"), 1));

        Assert.Equal(Now, report.GeneratedAt);
        Assert.Equal("test-machine", report.Machine);
        Assert.Equal(3, report.InventoryCounts.Values.Sum());
        Assert.Equal(2, report.InventoryCounts["vault"]);
        Assert.Equal(1, report.InventoryCounts["skills"]);
    }
}
