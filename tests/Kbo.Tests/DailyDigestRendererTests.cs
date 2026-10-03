using Kbo.Gold;

namespace Kbo.Tests;

public class DailyDigestRendererTests
{
    private static DayDigest Day(string date, long sessions = 5, long touched = 2)
    {
        return new DayDigest(
            date, sessions, touched, sessions == 0 ? 0 : (double)touched / sessions,
            ByAgent: [new DayCount("claude-code", 3), new DayCount("opencode", 2)],
            ByRepo: [new DayCount("/home/u/RepoA", 4), new DayCount("(unknown)", 1)],
            ReadsByLayer: [new DayCount("local", 12), new DayCount("global", 3)],
            TotalReads: 15,
            Searches: 20,
            SearchHits: 16,
            SearchZeroHits: 4,
            TopZeroHitQueries: [new DayCount("missing thing", 3)],
            InputTokens: 1500,
            CacheReadTokens: 90000,
            SkillsUsed: [new DayCount("tdd", 4), new DayCount("brainstorming", 1)],
            SessionDetail:
            [
                new DaySession("18:13", "claude-code", "/home/u/RepoA", 8, 4, 1, 0, TouchedKb: true, 0, 260000),
            ]);
    }

    [Fact]
    public void RenderDay_ContainsAllSectionsAndNumbers()
    {
        string markdown = DailyDigestRenderer.RenderDay(Day("2026-08-13"));

        Assert.Contains("# 2026-08-13", markdown, StringComparison.Ordinal);
        Assert.Contains("[[index|", markdown, StringComparison.Ordinal);
        Assert.Contains("claude-code", markdown, StringComparison.Ordinal);
        Assert.Contains("/home/u/RepoA", markdown, StringComparison.Ordinal);
        Assert.Contains("40%", markdown, StringComparison.Ordinal);              // KB-touch 2/5
        Assert.Contains("missing thing", markdown, StringComparison.Ordinal);    // top zero-hit query
        Assert.Contains("20%", markdown, StringComparison.Ordinal);              // miss rate 4/20
        Assert.Contains("local", markdown, StringComparison.Ordinal);
        Assert.Contains("Skills used", markdown, StringComparison.Ordinal);
        Assert.Contains("tdd", markdown, StringComparison.Ordinal);
        Assert.Contains("Per session", markdown, StringComparison.Ordinal);
        Assert.Contains("18:13", markdown, StringComparison.Ordinal);
        Assert.Contains("RepoA", markdown, StringComparison.Ordinal);        // repo shown as last path segment
    }

    [Fact]
    public void RenderIndex_ListsDaysNewestFirstWithWikilinks()
    {
        string markdown = DailyDigestRenderer.RenderIndex([Day("2026-08-13"), Day("2026-08-11")]);

        Assert.Contains("[[2026-08-13]]", markdown, StringComparison.Ordinal);
        Assert.Contains("[[2026-08-11]]", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("2026-08-13", StringComparison.Ordinal)
            < markdown.IndexOf("2026-08-11", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderDay_EscapesPipesInQueriesSoTablesDoNotBreak()
    {
        DayDigest day = Day("2026-08-13") with { TopZeroHitQueries = [new DayCount("a|b pattern", 1)] };

        string markdown = DailyDigestRenderer.RenderDay(day);

        Assert.DoesNotContain("a|b pattern", markdown, StringComparison.Ordinal);
        Assert.Contains("a\\|b pattern", markdown, StringComparison.Ordinal);
    }
}
