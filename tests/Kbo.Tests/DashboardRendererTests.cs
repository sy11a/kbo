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
            SddPanel: new SddPanelGold(
                [new SddOrderingRow("2026-W33", "/home/u/Repository/RepoA", 4, 2, 0.5)],
                new SddOrderingSummary(4, 2, 0.5),
                [new SddWritesRow("knowledge", 18), new SddWritesRow("code", 240)],
                MachineManagedWrites: 12,
                SkillRate: [new SddSkillRateRow("/home/u/Repository/RepoA", 6, 3, 0.5)],
                SkillConfigured: true),
            FailedSearchDaily: [new FailedSearchRow("2026-08-10", 4, 1, 0.25)],
            TokensDaily: [new TokensRow("2026-08-10", 1500, 900000)],
            UnusedThemes: [new ThemeReadsRow("vault/ideas", "vault", 0, 12)],
            RecentSessions:
            [
                new RecentSessionRow("2026-08-12", "09:00", "opencode", "/home/u/Repository/RepoA",
                    46, 12, 1, 0, true, 125000, 537000),
            ],
            TopFailedSearches: [new DayCount("duckdb window function", 4)],
            TopReusedNotes: [new ReuseRow("/home/u/Knowledge/core.md", 15, 42)],
            Reuse: new ReuseSummary(80, 48, 0.6),
            TopWriteReadNotes: [new WriteReadRow("/home/u/Knowledge/made.md", 9)],
            WriteReadLoop: new WriteReadSummary(30, 21, 0.7),
            Mirror: new PracticeMirrorGold(
            [
                new MirrorTile("Cache discipline · 14д", "93%", "в коридоре 90–98%", "контекст переиспользуется — норма", "ok",
                    "🟢", Goal: null, CorridorLow: 0.90, CorridorHigh: 0.98, HistoryWeeks: 8),
                new MirrorTile("Failed-search · 14д", "28%", "в коридоре 20–32%",
                    "линкуй заметки от слов, которыми ищешь (хроника — кандидат в бэклог)", "sick",
                    "🔴", Goal: "цель ≤15% · до цели −13пп", CorridorLow: 0.20, CorridorHigh: 0.32, HistoryWeeks: 8),
                new MirrorTile("Write→read loop · 6 нед", "12%", "история 2/6 нед",
                    "плитка ждёт достаточно своей истории", "wait", "⏳", HistoryWeeks: 2),
                new MirrorTile("Spec-before-code · 6 нед", "41%", "острый выход: z = 2.3",
                    "требует внимания сейчас — острый слом против своей нормы", "acute", "⚠️",
                    Goal: "цель ≥50% · до цели −9пп", CorridorLow: 0.35, CorridorHigh: 0.55, HistoryWeeks: 8),
            ]));
    }

    [Fact]
    public void Render_CarriesGeneratedAtStripLineAndInjectedData()
    {
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.Contains("2026-08-12T22:00:00Z", html);
        Assert.Contains("test-machine", html);

        Assert.Contains("Dead-man: 1/2 ok", html);
        Assert.Contains("oldest backup 5.9d / limit 3d", html);
        Assert.Contains("✗ SILENT", html);
        Assert.Contains("5.9d silent", html);

        Assert.Contains("\"cacheReadTokens\":900000", html);
        Assert.Contains("vegaEmbed(\"#failed-search-rate\"", html);
        Assert.Contains("vegaEmbed(\"#tokens-trend\"", html);
        Assert.Contains("integrity=\"sha384-", html);

        Assert.Contains("Practice mirror", html);
        Assert.Contains("Cache discipline", html);
        Assert.Contains("Write", html);
        Assert.Contains("tile red", html);
    }

    [Fact]
    public void Render_PracticeMirror_ShowsEmojiStatesGoalLinesAndPlaceholder()
    {
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.Contains(Enc("🟢 Cache discipline · 14д"), html);
        Assert.Contains(Enc("🔴 Failed-search · 14д"), html);
        Assert.Contains(Enc("цель ≤15% · до цели −13пп"), html);
        Assert.Contains(Enc("⚠️ Spec-before-code"), html);
        Assert.Contains("острый выход: z = 2.3", html);
        Assert.Contains(Enc("⏳ Write→read loop · 6 нед"), html);
        Assert.Contains("история 2/6 нед", html);
        Assert.Contains(Enc("в коридоре 20–32%"), html);
        Assert.Contains("tile sick", html);
        Assert.Contains("tile wait", html);
        Assert.Contains("Состояние = эмодзи, не цвет", html);
    }

    [Fact]
    public void Render_PracticeMirror_AmberClassOnlyOnAcuteTiles()
    {
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.Equal(1, CountOccurrences(html, "tile amber"));
        Assert.Equal(1, CountOccurrences(html, Enc("⚠️ Spec-before-code")));
        // 🔴 is a state, not an alarm: the sick tile carries no warning styling
        Assert.DoesNotContain("tile red", html[..html.IndexOf(Enc("🔴"), StringComparison.Ordinal)]);
    }

    private static string Enc(string value) => System.Net.WebUtility.HtmlEncode(value);

    private static int CountOccurrences(string text, string fragment)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }
        return count;
    }

    [Fact]
    public void Render_PracticeMirrorIsNull_SectionOmitted_NoCrash()
    {
        DashboardGold gold = Gold() with { Mirror = null };
        string html = DashboardRenderer.Render(gold, DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.DoesNotContain("Practice mirror", html);
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
            ConstitutionFleet: null,
            ServiceSessions: new ServiceSessionsSummary(0, ""),
            SddPanel: new SddPanelGold(
                [], new SddOrderingSummary(0, 0, 0), [], 0, [], SkillConfigured: false),
            FailedSearchDaily: [],
            TokensDaily: [],
            UnusedThemes: [new ThemeReadsRow("vault/x<img src=x onerror=alert(1)>", "vault", 0, 1)],
            RecentSessions:
            [
                new RecentSessionRow("2026-08-12", "09:00", "cc<script>alert(3)</script>", "/r",
                    1, 0, 0, 0, false, 0, 0),
            ],
            TopFailedSearches: [new DayCount("q<script>alert(5)</script>", 1)],
            TopReusedNotes: [new ReuseRow("/n/<script>alert(6)</script>.md", 2, 3)],
            Reuse: new ReuseSummary(1, 0, 0),
            TopWriteReadNotes: [new WriteReadRow("/w/<script>alert(7)</script>.md", 1)],
            WriteReadLoop: new WriteReadSummary(1, 1, 1.0));

        string html = DashboardRenderer.Render(hostile, DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("evil&lt;script&gt;", html);
        Assert.Contains("job&lt;img", html);
        Assert.Contains("vault/x&lt;img", html);
        Assert.DoesNotContain("<script>alert(3)</script>", html);
        Assert.Contains("cc&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(5)</script>", html);
        Assert.Contains("q&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(6)</script>", html);
        Assert.Contains("/n/&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(7)</script>", html);
        Assert.Contains("/w/&lt;script&gt;", html);
    }

    [Fact]
    public void Render_FleetNeverRenders_EvenWhenConfigured()
    {
        // per R-007 + R-009 — the fleet panel is cut from RENDER; its summary
        // lives on the report stdout line and in gold json only.
        Assert.NotNull(Gold().ConstitutionFleet);
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.DoesNotContain("Constitution fleet", html);
        Assert.DoesNotContain("fleet.sh upgrade", html);
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
    public void Render_SddPanel_ShowsOrderingWritesAndSkillRate()
    {
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.Contains("SDD practice — spec before code", html);
        Assert.Contains("2026-W33", html);
        Assert.Contains("/home/u/Repository/RepoA", html);
        Assert.Contains("50%", html);
        // machine-managed disclosure (no-silent-caps)
        Assert.Contains("12", html);
        Assert.Contains("ADR-0040", html);
    }

    [Fact]
    public void Render_SddPanel_UnconfiguredSkillRate_StatesTheAbsence()
    {
        string html = DashboardRenderer.Render(
            Gold() with
            {
                SddPanel = new SddPanelGold(
                    [], new SddOrderingSummary(0, 0, 0), [], 0, [], SkillConfigured: false),
            },
            DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.Contains("SDD practice — spec before code", html);
        Assert.Contains("sdd: { skills:", html);
    }

    [Fact]
    public void Render_NoServiceSessions_OmitsTheNote()
    {
        string html = DashboardRenderer.Render(Gold() with { ServiceSessions = new ServiceSessionsSummary(0, "") },
            DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.DoesNotContain("Служебные сессии", html);
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
    public void EmbeddedChartSpecs_ContainTheTwoSurvivingCharts()
    {
        IReadOnlyDictionary<string, string> specs = DashboardRenderer.LoadEmbeddedChartSpecs();

        Assert.Equal(2, specs.Count);
        Assert.Contains("failed-search-rate.vl.json", specs.Keys);
        Assert.Contains("tokens-trend.vl.json", specs.Keys);
    }

    [Fact]
    public void Render_CutSections_AreAbsentEverywhere()
    {
        // per R-007 — the declutter cut list must not resurrect anywhere.
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.DoesNotContain("This week vs last week", html);
        Assert.DoesNotContain("Sessions by repository", html);
        Assert.DoesNotContain("Top skills used", html);
        Assert.DoesNotContain("Reads by content type", html);
        Assert.DoesNotContain("Most-read knowledge themes", html);
        Assert.DoesNotContain("vegaEmbed(\"#reads-over-time\"", html);
        Assert.DoesNotContain("vegaEmbed(\"#reads-by-theme\"", html);
        Assert.DoesNotContain("vegaEmbed(\"#kb-touch-rate\"", html);
        Assert.DoesNotContain("Share of sessions touching", html);
    }

    [Fact]
    public void Render_SectionsFollowMirrorTileOrder()
    {
        // per R-001 — sections appear in the mirror-tile order defined by the
        // 2026-08-27 design session.
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        int mirror = html.IndexOf("Practice mirror", StringComparison.Ordinal);
        int deadMan = html.IndexOf("Dead-man health", StringComparison.Ordinal);
        int lastSeen = html.IndexOf("<details>", StringComparison.Ordinal);
        int sdd = html.IndexOf("SDD practice", StringComparison.Ordinal);
        int reuse = html.IndexOf("Most-reused knowledge notes", StringComparison.Ordinal);
        int unused = html.IndexOf("Never-read themes", StringComparison.Ordinal);
        int loop = html.IndexOf("Write → read loop", StringComparison.Ordinal);
        int failed = html.IndexOf("vegaEmbed(\"#failed-search-rate\"", StringComparison.Ordinal);
        int zeroHit = html.IndexOf("Top zero-hit searches", StringComparison.Ordinal);
        int tokens = html.IndexOf("vegaEmbed(\"#tokens-trend\"", StringComparison.Ordinal);
        int recent = html.IndexOf("Recent sessions", StringComparison.Ordinal);

        int[] positions = [mirror, deadMan, lastSeen, sdd, reuse, unused, loop, failed, zeroHit, tokens, recent];
        Assert.All(positions, position => Assert.True(position >= 0, "every surviving section must render"));
        Assert.Equal(positions, positions.OrderBy(position => position).ToArray());
    }

    [Fact]
    public void Render_DeadManAllHealthy_SingleStripLine_NoTiles()
    {
        // per R-002 — green jobs are silence: one strip line, no tile grid.
        DashboardGold gold = Gold() with
        {
            JobHealth =
            [
                new JobHealthTile("test-machine", "kbo", "harvest",
                    DateTimeOffset.Parse("2026-08-12T00:10:00Z", CultureInfo.InvariantCulture), 0.9, "ok"),
                new JobHealthTile("test-machine", "kbo", "report",
                    DateTimeOffset.Parse("2026-08-10T00:10:00Z", CultureInfo.InvariantCulture), 2.9, "ok"),
            ],
        };
        string html = DashboardRenderer.Render(gold, DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.Contains("Dead-man: 2/2 ok", html);
        Assert.Contains("oldest report 2.9d / limit 9.5d", html);
        Assert.DoesNotContain("✗ SILENT", html);
        Assert.DoesNotContain("harvest", html);
    }

    [Fact]
    public void Render_DeadManRedJob_RestoresItsTile()
    {
        // per R-003 — a red job returns as a full tile beside the strip.
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.Contains("Dead-man: 1/2 ok", html);
        Assert.Contains("✗ SILENT", html);
        Assert.Contains("5.9d silent", html);
        Assert.Contains(Enc("test-machine · kbo"), html);
    }

    [Fact]
    public void Render_LastSeen_CollapsedIntoDetails()
    {
        // per R-004 — provenance detail lives behind a closed details element.
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        int detailsStart = html.IndexOf("<details>", StringComparison.Ordinal);
        Assert.True(detailsStart >= 0);
        string details = html[detailsStart..html.IndexOf("</details>", StringComparison.Ordinal)];
        Assert.Contains("Last seen in bronze — 1 agent(s) · newest 0d ago", details[..details.IndexOf("</summary>")]);
        Assert.Contains("последний раз", html);
    }

    [Fact]
    public void Render_UnusedThemesList_StaysAfterReuse()
    {
        string html = DashboardRenderer.Render(Gold(), DashboardRenderer.LoadEmbeddedChartSpecs());

        Assert.Contains("Never-read themes", html);
        Assert.Contains("vault/ideas", html);
        Assert.Contains("Recent sessions", html);
        Assert.Contains("2026-08-12 09:00", html);
        Assert.Contains("125k/537k", html);
        Assert.Contains("Top zero-hit searches", html);
        Assert.Contains("duckdb window function", html);
        Assert.Contains("Most-reused knowledge notes", html);
        Assert.Contains("/home/u/Knowledge/core.md", html);
        Assert.Contains("60%", html);   // single-use ratio 48/80
        Assert.Contains("Write → read loop", html);
        Assert.Contains("/home/u/Knowledge/made.md", html);
        Assert.Contains("70%", html);   // loop rate 21/30
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
