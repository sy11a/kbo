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
        IReadOnlyDictionary<string, int>? machineManaged = null,
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
            new Dictionary<string, int>(StringComparer.Ordinal) { ["vault"] = 10, ["skills"] = 4 },
            lifecycle ?? new Dictionary<string, int>(StringComparer.Ordinal),
            machineManaged ?? new Dictionary<string, int>(StringComparer.Ordinal),
            dormant ?? [],
            dead ?? [],
            hot ?? [],
            stale ?? []);
    }

    [Fact]
    public void Render_CarriesBannerAndGeneratedAt()
    {
        string markdown = MarkdownRenderer.Render(Report(), "/home/u/Knowledge");

        Assert.Contains("GENERATED", markdown, StringComparison.Ordinal);
        Assert.Contains("2026-08-12T12:00:00Z", markdown, StringComparison.Ordinal);
        Assert.Contains("test-machine", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_VaultNotesBecomeWikilinks_SkillPathsStayPlain()
    {
        DeadNote vaultNote = new(
            "/home/u/Knowledge/homelab/Hardening Audit.md", "vault", "global", 120, LastRead: null, ["archive"]);
        DeadNote skillNote = new(
            "/home/u/.claude/skills/tdd/SKILL.md", "skills", "skills", 95, LastRead: null, ["retire"]);

        string markdown = MarkdownRenderer.Render(Report(dead: [vaultNote, skillNote]), "/home/u/Knowledge");

        Assert.Contains("[[homelab/Hardening Audit]]", markdown, StringComparison.Ordinal);
        Assert.Contains("`/home/u/.claude/skills/tdd/SKILL.md`", markdown, StringComparison.Ordinal);
        Assert.Contains("archive", markdown, StringComparison.Ordinal);
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
            Report(lifecycle: new Dictionary<string, int>(StringComparer.Ordinal) { ["repo-SomeApp"] = 46 }),
            "/home/u/Knowledge");

        Assert.Contains("## Lifecycle artifacts", markdown, StringComparison.Ordinal);
        Assert.Contains("`repo-SomeApp`: 46 note(s)", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DormantSourcesSection_ListsSourceWithWithheldCount()
    {
        DormantSource dormant = new(
            "repo-SomeApp",
            DateTimeOffset.Parse("2026-07-15T10:00:00Z", CultureInfo.InvariantCulture),
            70);

        string markdown = MarkdownRenderer.Render(Report(dormant: [dormant]), "/home/u/Knowledge");

        Assert.Contains("## Dormant sources", markdown, StringComparison.Ordinal);
        Assert.Contains("`repo-SomeApp`", markdown, StringComparison.Ordinal);
        Assert.Contains("| 70 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MachineManagedSection_ListsPerSourceCounts()
    {
        string markdown = MarkdownRenderer.Render(
            Report(machineManaged: new Dictionary<string, int>(StringComparer.Ordinal) { ["repo-app"] = 12 }),
            "/home/u/Knowledge");

        Assert.Contains("## Machine-managed files", markdown, StringComparison.Ordinal);
        Assert.Contains("`repo-app`: 12 file(s)", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_HotAndStaleRows_CarryCounts()
    {
        HotNote hot = new("/home/u/Knowledge/hot.md", "vault", 12, 40,
            DateTimeOffset.Parse("2026-08-11T09:00:00Z", CultureInfo.InvariantCulture));
        StaleNote stale = new("/home/u/Knowledge/stale.md", "vault", 5, 200);

        string markdown = MarkdownRenderer.Render(Report(hot: [hot], stale: [stale]), "/home/u/Knowledge");

        Assert.Contains("[[hot]]", markdown, StringComparison.Ordinal);
        Assert.Contains("12", markdown, StringComparison.Ordinal);
        Assert.Contains("[[stale]]", markdown, StringComparison.Ordinal);
        Assert.Contains("200", markdown, StringComparison.Ordinal);
    }
}
