using System.Globalization;
using Kbo.Gold;

namespace Kbo.Tests;

public class DashboardRendererTests
{
    private static DashboardGold Gold()
    {
        return new DashboardGold(
            DateTimeOffset.Parse("2026-08-12T22:00:00Z", CultureInfo.InvariantCulture),
            "test-machine",
            DeadManThresholdDays: 3,
            WeeklyDeadManThresholdDays: 9.5,
            JobHealth:
            [
                new JobHealthTile("test-machine", "kbo", "harvest",
                    DateTimeOffset.Parse("2026-08-12T00:10:00Z", CultureInfo.InvariantCulture), 0.9, "ok"),
                new JobHealthTile("test-machine", "kbo", "backup",
                    DateTimeOffset.Parse("2026-08-07T00:10:00Z", CultureInfo.InvariantCulture), 5.9, "red"),
            ],
            LastSeen:
            [
                new LastSeenTile("test-machine", "claude-code",
                    DateTimeOffset.Parse("2026-08-12T21:00:00Z", CultureInfo.InvariantCulture), 0.0, "ok"),
            ],
            ConstitutionFleet: new ConstitutionFleetGold(15,
            [
                new FleetRepoTile("/home/u/Repository/RepoA", "15", "ok"),
                new FleetRepoTile("/home/u/Repository/RepoB", "14", "red"),
            ], 1),
            ServiceSessions: new ServiceSessionsSummary(7, "service-fleet"),
            ReadsByLayerDaily: [new ReadsByLayerRow("2026-08-10", "global", 12)],
            FailedSearchDaily: [new FailedSearchRow("2026-08-10", 4, 1, 0.25)],
            KbTouchDaily: [new KbTouchRow("2026-08-10", 8, 3, 0.375)],
            TokensDaily: [new TokensRow("2026-08-10", 1500, 900000)],
            ThemeReads: [new ThemeReadsRow("vault/rituals", "vault", 42, 7)],
            UnusedThemes: [new ThemeReadsRow("vault/ideas", "vault", 0, 12)],
            SessionsByRepo:
            [
                new RepoSessionsRow("/home/u/Repository/RepoA", 21, "claude-code, opencode",
                    DateTimeOffset.Parse("2026-08-12T09:00:00Z", CultureInfo.InvariantCulture)),
            ],
            RecentSessions:
            [
                new RecentSessionRow("2026-08-12", "09:00", "opencode", "/home/u/Repository/RepoA",
                    46, 12, 1, 0, true, 125000, 537000),
            ],
            TopSkills: [new DayCount("tdd", 12), new DayCount("brainstorming", 5)],
            TopFailedSearches: [new DayCount("duckdb window function", 4)],
            ReadsByContentType: [new DayCount("knowledge", 2935), new DayCount("code", 1147)],
            TopReusedNotes: [new ReuseRow("/home/u/Knowledge/core.md", 15, 42)],
            Reuse: new ReuseSummary(80, 48, 0.6),
            TopWriteReadNotes: [new WriteReadRow("/home/u/Knowledge/made.md", 9)],
            WriteReadLoop: new WriteReadSummary(30, 21, 0.7),
            WeekOverWeek:
            [
                new MetricDelta("KB-touch rate", 0.25, 0.15, "percent", true),
                new MetricDelta("Knowledge reads", 120, 80, "count", true),
            ]);
    }

    [Fact]
    public void Render_CarriesGeneratedAtTilesAndInjectedData()
    {
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.Contains("2026-08-12T22:00:00Z", html);
        Assert.Contains("test-machine", html);

        Assert.Contains("✓ ok", html);
        Assert.Contains("✗ SILENT", html);
        Assert.Contains("backup", html);
        Assert.Contains("5.9d silent", html);

        Assert.Contains("\"layer\":\"global\"", html);
        Assert.Contains("\"reads\":12", html);
        Assert.Contains("\"cacheReadTokens\":900000", html);
        Assert.Contains("vegaEmbed(\"#reads-over-time\"", html);
        Assert.Contains("integrity=\"sha384-", html);
    }

    [Fact]
    public void Render_HtmlEncodesTileText_NoMarkupInjection()
    {
        DashboardGold hostile = new(
            DateTimeOffset.Parse("2026-08-12T22:00:00Z", CultureInfo.InvariantCulture),
            "evil<script>alert(1)</script>machine",
            DeadManThresholdDays: 3,
            WeeklyDeadManThresholdDays: 9.5,
            JobHealth:
            [
                new JobHealthTile("m", "a", "job<img src=x onerror=alert(1)>",
                    DateTimeOffset.Parse("2026-08-12T00:00:00Z", CultureInfo.InvariantCulture), 0.1, "ok"),
            ],
            LastSeen: [],
            ConstitutionFleet: new ConstitutionFleetGold(15,
                [new FleetRepoTile("/r/<script>alert(8)</script>", "14", "red")], 1),
            ServiceSessions: new ServiceSessionsSummary(0, ""),
            ReadsByLayerDaily: [],
            FailedSearchDaily: [],
            KbTouchDaily: [],
            TokensDaily: [],
            ThemeReads: [],
            UnusedThemes: [new ThemeReadsRow("vault/x<img src=x onerror=alert(1)>", "vault", 0, 1)],
            SessionsByRepo:
            [
                new RepoSessionsRow("/evil/<script>alert(2)</script>", 1, "claude-code",
                    DateTimeOffset.Parse("2026-08-12T09:00:00Z", CultureInfo.InvariantCulture)),
            ],
            RecentSessions:
            [
                new RecentSessionRow("2026-08-12", "09:00", "cc<script>alert(3)</script>", "/r",
                    1, 0, 0, 0, false, 0, 0),
            ],
            TopSkills: [new DayCount("sk<script>alert(4)</script>", 1)],
            TopFailedSearches: [new DayCount("q<script>alert(5)</script>", 1)],
            ReadsByContentType: [new DayCount("knowledge", 1)],
            TopReusedNotes: [new ReuseRow("/n/<script>alert(6)</script>.md", 2, 3)],
            Reuse: new ReuseSummary(1, 0, 0),
            TopWriteReadNotes: [new WriteReadRow("/w/<script>alert(7)</script>.md", 1)],
            WriteReadLoop: new WriteReadSummary(1, 1, 1.0),
            WeekOverWeek: [new MetricDelta("KB-touch rate", 0.1, 0.2, "percent", true)]);

        string html = DashboardRenderer.Render(hostile, DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("evil&lt;script&gt;", html);
        Assert.Contains("job&lt;img", html);
        Assert.Contains("vault/x&lt;img", html);
        Assert.DoesNotContain("<script>alert(2)</script>", html);
        Assert.Contains("/evil/&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(3)</script>", html);
        Assert.Contains("cc&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(4)</script>", html);
        Assert.DoesNotContain("<script>alert(5)</script>", html);
        Assert.Contains("sk&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(6)</script>", html);
        Assert.Contains("/n/&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(7)</script>", html);
        Assert.Contains("/w/&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(8)</script>", html);
        Assert.Contains("/r/&lt;script&gt;", html);
    }

    [Fact]
    public void Render_ConstitutionFleet_ListsReposWithStates()
    {
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.Contains("Constitution fleet — skill v15", html);
        Assert.Contains("/home/u/Repository/RepoA", html);
        Assert.Contains("""<td class="good">✓ v15</td>""", html);
        Assert.Contains("""<td class="bad">✗ v14 — behind</td>""", html);
        Assert.Contains("fleet.sh upgrade", html);
    }

    [Fact]
    public void Render_ServiceSessionNote_StatesTheExclusion()
    {
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.Contains("Служебные сессии: 7", html);
        Assert.Contains("service-fleet", html);
        Assert.Contains("ADR-0039", html);
    }

    [Fact]
    public void Render_NoServiceSessions_OmitsTheNote()
    {
        string html = DashboardRenderer.Render(Gold() with { ServiceSessions = new ServiceSessionsSummary(0, "") },
            DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.DoesNotContain("Служебные сессии", html);
    }

    [Fact]
    public void Render_WithoutConstitutionConfig_OmitsFleetSection()
    {
        string html = DashboardRenderer.Render(Gold() with { ConstitutionFleet = null },
            DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.DoesNotContain("Constitution fleet", html);
    }

    [Fact]
    public void Render_WithAutoReloadSeconds_EmitsRefreshMetaInHead()
    {
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs(), autoReloadSeconds: 20);

        Assert.Contains("<meta http-equiv=\"refresh\" content=\"20\">", html);
    }

    [Fact]
    public void Render_WithoutAutoReload_HasNoRefreshMeta()
    {
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.DoesNotContain("http-equiv=\"refresh\"", html);
    }

    [Fact]
    public void EmbeddedChartSpecs_ContainAllFiveCharts()
    {
        IReadOnlyDictionary<string, string> specs = DashboardRenderer.LoadEmbeddedChartSpecs();

        Assert.Equal(5, specs.Count);
        Assert.Contains("reads-over-time.vl.json", specs.Keys);
        Assert.Contains("tokens-trend.vl.json", specs.Keys);
        Assert.Contains("reads-by-theme.vl.json", specs.Keys);
    }

    [Fact]
    public void Render_EmbedsThemeChartAndUnusedThemesList()
    {
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.Contains("vegaEmbed(\"#reads-by-theme\"", html);
        Assert.Contains("\"theme\":\"vault/rituals\"", html);
        Assert.Contains("\"reads\":42", html);
        Assert.Contains("vault/ideas", html);
        Assert.Contains("12", html);
    }

    [Fact]
    public void Render_ShowsSessionsByRepoTable_WithFullPaths()
    {
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.Contains("Sessions by repository", html);
        Assert.Contains("/home/u/Repository/RepoA", html);
        Assert.Contains("claude-code, opencode", html);
        Assert.Contains("Источник данных", html);

        Assert.Contains("Recent sessions", html);
        Assert.Contains("2026-08-12 09:00", html);
        Assert.Contains("125k/537k", html);

        Assert.Contains("Top skills used", html);
        Assert.Contains("brainstorming", html);
        Assert.Contains("Top zero-hit searches", html);
        Assert.Contains("duckdb window function", html);
        Assert.Contains("Reads by content type", html);
        Assert.Contains("knowledge", html);
        Assert.Contains("Most-reused knowledge notes", html);
        Assert.Contains("/home/u/Knowledge/core.md", html);
        Assert.Contains("60%", html);   // single-use ratio 48/80
        Assert.Contains("Write → read loop", html);
        Assert.Contains("/home/u/Knowledge/made.md", html);
        Assert.Contains("70%", html);   // loop rate 21/30
        Assert.Contains("This week vs last week", html);
        Assert.Contains("+10pp", html);   // KB-touch 25% vs 15%
        Assert.Contains("class=\"good\"", html);
    }

    [Fact]
    public void Render_ShowsRussianDescriptionForEveryChartFromSpecUsermeta()
    {
        IReadOnlyDictionary<string, string> specs = DashboardRenderer.LoadEmbeddedChartSpecs();
        string html = DashboardRenderer.Render(Gold(), specs);

        foreach ((string name, string specJson) in specs)
        {
            string? russian = System.Text.Json.Nodes.JsonNode.Parse(specJson)?["usermeta"]?["kbo"]?["ru"]?.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(russian), $"{name} must carry a Russian description in usermeta.kbo.ru");
            Assert.Contains(System.Net.WebUtility.HtmlEncode(russian), html);
        }

        Assert.Contains("Здоровье фоновых задач", html);
        Assert.Contains("последний раз", html);
    }
}
