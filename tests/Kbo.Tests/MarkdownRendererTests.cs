using System.Globalization;
using Kbo.Gold;

namespace Kbo.Tests;

public class MarkdownRendererTests
{
    private static GoldReport Report(
        IReadOnlyList<DeadNote>? dead = null,
        IReadOnlyList<HotNote>? hot = null,
        IReadOnlyList<StaleNote>? stale = null,
        IReadOnlyDictionary<string, int>? lifecycle = null,
        IReadOnlyList<DormantSource>? dormant = null)
    {
        return new GoldReport(
            DateTimeOffset.Parse("2026-08-12T12:00:00Z", CultureInfo.InvariantCulture),
            "test-machine",
            MinInventoryAgeDays: 30,
            ReadWindowDays: 60,
            StaleMinReads: 3,
            StaleUnmodifiedDays: 90,
            DormantAfterDays: 21,
            new Dictionary<string, int> { ["vault"] = 10, ["skills"] = 4 },
            lifecycle ?? new Dictionary<string, int>(),
            dormant ?? [],
            dead ?? [],
            hot ?? [],
            stale ?? []);
    }

    [Fact]
    public void Render_CarriesBannerAndGeneratedAt()
    {
        string markdown = MarkdownRenderer.Render(Report(), "/home/u/Knowledge");

        Assert.Contains("GENERATED", markdown);
        Assert.Contains("2026-08-12T12:00:00Z", markdown);
        Assert.Contains("test-machine", markdown);
    }

    [Fact]
    public void Render_VaultNotesBecomeWikilinks_SkillPathsStayPlain()
    {
        DeadNote vaultNote = new(
            "/home/u/Knowledge/homelab/Hardening Audit.md", "vault", "global", 120, null, ["archive"]);
        DeadNote skillNote = new(
            "/home/u/.claude/skills/tdd/SKILL.md", "skills", "skills", 95, null, ["retire"]);

        string markdown = MarkdownRenderer.Render(Report(dead: [vaultNote, skillNote]), "/home/u/Knowledge");

        Assert.Contains("[[homelab/Hardening Audit]]", markdown);
        Assert.Contains("`/home/u/.claude/skills/tdd/SKILL.md`", markdown);
        Assert.Contains("archive", markdown);
    }

    [Fact]
    public void Render_EmptySections_SayNone()
    {
        string markdown = MarkdownRenderer.Render(Report(), "/home/u/Knowledge");

        Assert.Contains("none", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_LifecycleCountsSection_ListsPerSourceCounts()
    {
        string markdown = MarkdownRenderer.Render(
            Report(lifecycle: new Dictionary<string, int> { ["repo-CareerPlatform"] = 46 }),
            "/home/u/Knowledge");

        Assert.Contains("## Lifecycle artifacts", markdown);
        Assert.Contains("`repo-CareerPlatform`: 46 note(s)", markdown);
    }

    [Fact]
    public void Render_DormantSourcesSection_ListsSourceWithWithheldCount()
    {
        DormantSource dormant = new(
            "repo-CareerPlatform",
            DateTimeOffset.Parse("2026-07-15T10:00:00Z", CultureInfo.InvariantCulture),
            70);

        string markdown = MarkdownRenderer.Render(Report(dormant: [dormant]), "/home/u/Knowledge");

        Assert.Contains("## Dormant sources", markdown);
        Assert.Contains("`repo-CareerPlatform`", markdown);
        Assert.Contains("| 70 |", markdown);
    }

    [Fact]
    public void Render_HotAndStaleRows_CarryCounts()
    {
        HotNote hot = new("/home/u/Knowledge/hot.md", "vault", 12, 40,
            DateTimeOffset.Parse("2026-08-11T09:00:00Z", CultureInfo.InvariantCulture));
        StaleNote stale = new("/home/u/Knowledge/stale.md", "vault", 5, 200);

        string markdown = MarkdownRenderer.Render(Report(hot: [hot], stale: [stale]), "/home/u/Knowledge");

        Assert.Contains("[[hot]]", markdown);
        Assert.Contains("12", markdown);
        Assert.Contains("[[stale]]", markdown);
        Assert.Contains("200", markdown);
    }
}
