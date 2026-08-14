using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Gold;
using Kbo.Registry;
using Kbo.Silver;

namespace Kbo.Tests;

public class DailyDigestComputerTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-12T23:00:00Z", CultureInfo.InvariantCulture);

    private readonly string workspace;
    private readonly string vaultRoot;
    private readonly string silverPath;
    private readonly KnowledgeRegistry registry;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public DailyDigestComputerTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-digest-tests").FullName;
        vaultRoot = Path.Combine(workspace, "Knowledge");
        silverPath = Path.Combine(workspace, "silver.duckdb");
        Directory.CreateDirectory(vaultRoot);
        registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {vaultRoot}
            """);
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private static JsonObject Event(string id, string type, string time, string? session, string agent = "claude-code",
        string? subject = null, JsonObject? data = null, string? repo = null)
    {
        JsonObject eventData = data ?? new JsonObject();
        eventData["origin"] = "harvest";
        eventData["transcript"] = "t-" + id[^2..];
        return new JsonObject
        {
            ["id"] = id,
            ["type"] = type,
            ["time"] = time,
            ["subject"] = subject,
            ["machine"] = "test-machine",
            ["agent"] = agent,
            ["session"] = session,
            ["repo"] = repo,
            ["kbroot"] = null,
            ["data"] = eventData,
        };
    }

    private IReadOnlyList<DayDigest> Compute(params JsonObject[] events)
    {
        string eventsRepo = Path.Combine(workspace, "kb-events");
        new BronzeStore(eventsRepo).Append(events);
        SilverRebuilder.Rebuild(eventsRepo, silverPath);
        return DailyDigestComputer.Compute(silverPath, registry, new FixedTimeProvider(Now));
    }

    [Fact]
    public void Digest_AggregatesSessionsTouchAndBreakdowns_ForTheDay()
    {
        string notePath = Path.Combine(vaultRoot, "note.md");
        IReadOnlyList<DayDigest> digests = Compute(
            Event("01G00000000000000000000001", "session.started", "2026-08-12T09:00:00Z", "s-a", agent: "claude-code",
                subject: "s-a", repo: "/home/u/RepoA", data: new JsonObject { ["branch"] = null, ["usage"] = null }),
            Event("01G00000000000000000000002", "knowledge.read", "2026-08-12T09:05:00Z", "s-a", agent: "claude-code",
                subject: notePath),
            Event("01G00000000000000000000003", "session.started", "2026-08-12T10:00:00Z", "s-b", agent: "opencode",
                subject: "s-b", repo: "/home/u/RepoB", data: new JsonObject { ["branch"] = null, ["usage"] = null }));

        DayDigest day = Assert.Single(digests, d => d.Date == "2026-08-12");
        Assert.Equal(2, day.Sessions);
        Assert.Equal(1, day.SessionsTouchingKb);
        Assert.Equal(0.5, day.KbTouchRate, precision: 3);
        Assert.Contains(day.ByAgent, entry => entry.Label == "claude-code" && entry.Count == 1);
        Assert.Contains(day.ByAgent, entry => entry.Label == "opencode" && entry.Count == 1);
        Assert.Contains(day.ByRepo, entry => entry.Label == "/home/u/RepoA" && entry.Count == 1);
        Assert.Contains(day.ReadsByLayer, entry => entry.Label == "global" && entry.Count == 1);
        Assert.Equal(1, day.TotalReads);

        DaySession sessionA = Assert.Single(day.SessionDetail, session => session.Repo == "/home/u/RepoA");
        Assert.Equal("claude-code", sessionA.Agent);
        Assert.Equal(1, sessionA.Reads);
        Assert.True(sessionA.TouchedKb);
        Assert.Contains(day.SessionDetail, session => session.Repo == "/home/u/RepoB" && !session.TouchedKb);
    }

    [Fact]
    public void Digest_CountsSearchesHitsMisses_AndTopZeroHitQueries()
    {
        IReadOnlyList<DayDigest> digests = Compute(
            Event("01G00000000000000000000004", "knowledge.searched", "2026-08-12T09:00:00Z", "s-a",
                subject: "missing term", data: new JsonObject { ["hits"] = 0 }),
            Event("01G00000000000000000000005", "knowledge.searched", "2026-08-12T09:10:00Z", "s-a",
                subject: "missing term", data: new JsonObject { ["hits"] = 0 }),
            Event("01G00000000000000000000006", "knowledge.searched", "2026-08-12T09:20:00Z", "s-a",
                subject: "found term", data: new JsonObject { ["hits"] = 5 }));

        DayDigest day = Assert.Single(digests, d => d.Date == "2026-08-12");
        Assert.Equal(3, day.Searches);
        Assert.Equal(1, day.SearchHits);
        Assert.Equal(2, day.SearchZeroHits);
        DayCount top = Assert.Single(day.TopZeroHitQueries);
        Assert.Equal("missing term", top.Label);
        Assert.Equal(2, top.Count);
    }

    [Fact]
    public void Digest_CountsSkillsUsedPerDay()
    {
        IReadOnlyList<DayDigest> digests = Compute(
            Event("01G0000000000000000000000A", "skill.invoked", "2026-08-12T09:00:00Z", "s-a",
                data: new JsonObject { ["skill"] = "tdd" }),
            Event("01G0000000000000000000000B", "skill.invoked", "2026-08-12T09:30:00Z", "s-a",
                data: new JsonObject { ["skill"] = "tdd" }),
            Event("01G0000000000000000000000C", "skill.invoked", "2026-08-12T10:00:00Z", "s-b",
                data: new JsonObject { ["skill"] = "brainstorming" }));

        DayDigest day = Assert.Single(digests, d => d.Date == "2026-08-12");
        Assert.Equal("tdd", day.SkillsUsed[0].Label);
        Assert.Equal(2, day.SkillsUsed[0].Count);
        Assert.Contains(day.SkillsUsed, skill => skill.Label == "brainstorming" && skill.Count == 1);
    }

    [Fact]
    public void Digest_SumsTokens_AndOrdersDaysNewestFirst()
    {
        IReadOnlyList<DayDigest> digests = Compute(
            Event("01G00000000000000000000007", "session.started", "2026-08-10T09:00:00Z", "s-old", agent: "claude-code",
                subject: "s-old", data: new JsonObject { ["branch"] = null, ["usage"] = null }),
            Event("01G00000000000000000000008", "session.started", "2026-08-12T09:00:00Z", "s-new", agent: "claude-code",
                subject: "s-new", data: new JsonObject
                {
                    ["branch"] = null,
                    ["usage"] = new JsonObject { ["input_tokens"] = 100, ["cache_read_tokens"] = 2000, ["output_tokens"] = 10 },
                }));

        Assert.Equal("2026-08-12", digests[0].Date);
        Assert.Equal("2026-08-10", digests[1].Date);
        Assert.Equal(100, digests[0].InputTokens);
        Assert.Equal(2000, digests[0].CacheReadTokens);
    }
}
