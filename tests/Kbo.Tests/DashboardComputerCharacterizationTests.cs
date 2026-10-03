using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Gold;
using Kbo.Registry;
using Kbo.Silver;

namespace Kbo.Tests;

/// <summary>
/// Pins the current output of the practice mirror and the SDD panel before their compute methods are split.
/// </summary>
public sealed class DashboardComputerCharacterizationTests : IDisposable
{
    private const string HintWait = "плитка ждёт достаточно своей истории";
    private const string HintAcute = "требует внимания сейчас — острый слом против своей нормы";
    private const string HintTrend = "устойчивый сдвиг — найди, что изменилось в практике";
    private const string HintNoData = "нечего измерять — окно пустое";
    private const string LabelCache = "Cache discipline · 14д";
    private const string LabelBurner = "Burner sessions · 14д";
    private const string LabelLoop = "Write→read loop · 6 нед";
    private const string LabelSingleUse = "Single-use notes · 6 нед";
    private const string LabelFailed = "Failed-search · 14д";
    private const string LabelSpec = "Spec-before-code · 6 нед";
    private const string RepoA = "/home/u/RepoA";
    private const string RepoB = "/home/u/RepoB";
    private const string Unknown = "(unknown)";
    private const string StatsFlatZero = "8|0.000000000|0.000000000|0.000000000|0.000000000";
    private const string StatsFlatThreeQuarters = "8|0.750000000|0.750000000|0.750000000|0.000000000";
    private const string StatsCache = "8|0.773128227|0.776470588|0.774478726|0.001991862";
    private const string StatsNone = "0|-|-|-|-";
    private const string StatsOneWeek = "1|-|-|-|-";
    private const string TrendNone = "нет данных в окне";
    private const string TrendOneWeek = "история 1/6 нед";
    private const string ValueNone = "нет данных";
    private const string TrendFlatZero = "в коридоре 0%–0%";
    private const string HintCache = "контекст переиспользуется — норма";
    private const string HintBurner = "одноразовых задач мало — норма";
    private const string GoalFailedReached = "цель ≤15% · достигнута";
    private const string GoalSpecReached = "цель ≥50% · достигнута";
    private const string Waiting = "wait";
    private const string StateWaiting = "⏳";
    private const string StateOk = "🟢";
    private const string StateAcute = "⚠️";
    private const string? NoGoal = null;
    private const int NoteWeeks = 11;

    private static readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-08-12T22:00:00Z", CultureInfo.InvariantCulture);

    private readonly string _workspace;
    private readonly string _silverPath;
    private int _runs;

    private sealed record Expect(
        string Label, string Value, string State, string Status, string Trend, string Hint, string? Goal, string Stats);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public DashboardComputerCharacterizationTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-dashboard-characterization").FullName;
        _silverPath = Path.Combine(_workspace, "silver.duckdb");
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private string NoteRoot => Path.Combine(_workspace, "Knowledge");

    private KnowledgeRegistry Registry(bool withSdd)
    {
        string sdd = withSdd ? "sdd:\n  skills:\n    - legislator\n    - superpowers:brainstorm\n" : string.Empty;
        return KnowledgeRegistry.Parse($"machine: test-machine\n{sdd}sources:\n  - id: vault\n    layer: global\n    root: {NoteRoot}\n");
    }

    private DashboardGold Compute(bool withSdd, IEnumerable<JsonObject> events)
    {
        string eventsRepo = Path.Combine(_workspace, string.Create(CultureInfo.InvariantCulture, $"kb-events-{_runs++}"));
        new BronzeStore(eventsRepo).Append([.. events]);
        _ = SilverRebuilder.Rebuild(eventsRepo, _silverPath);
        return DashboardComputer.Compute(_silverPath, Registry(withSdd), new FixedTimeProvider(_now));
    }

    private DashboardGold ComputeMirror(MirrorShape shape) => Compute(withSdd: false, new MirrorEventBuilder(NoteRoot).Build(shape));

    private DashboardGold ComputeEmpty()
    {
        JsonObject job = DashboardTestEvents.Event("01F00000000000000000000099", "job.completed", "2026-08-12T00:10:00Z",
            subject: "harvest", session: null, agent: "kbo", data: new JsonObject { ["job"] = "harvest", ["duration_ms"] = 5 });
        return Compute(withSdd: true, [job]);
    }

    private static int[] Fill(int value) => [.. Enumerable.Repeat(value, NoteWeeks)];

    private static MirrorShape SteadyShape()
    {
        return new MirrorShape(
            CacheA: [300000, 200000, 250000, 300000, 220000, 260000, 240000, 280000, 230000, 250000, 260000],
            Written: Fill(value: 4),
            Reused: [1, 1, 1, 1, 2, 2, 2, 3, 3, 4, 4],
            MultiRead: [0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2],
            ZeroSearches: [3, 2, 4, 3, 2, 4, 3, 3, 4, 3, 3],
            CodeSessions: Fill(value: 4),
            SpecFirst: Fill(value: 3));
    }

    private static MirrorShape BreakShape()
    {
        return new MirrorShape(
            CacheA: [300000, 200000, 250000, 300000, 220000, 260000, 240000, 280000, 230000, 0, 0],
            Written: [0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 2],
            Reused: [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1],
            MultiRead: Fill(value: 0),
            ZeroSearches: [9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 0],
            CodeSessions: Fill(value: 0),
            SpecFirst: Fill(value: 0));
    }

    private static MirrorShape CodeOnlyShape()
    {
        return new MirrorShape(
            CacheA: Fill(value: 250000),
            Written: Fill(value: 0),
            Reused: Fill(value: 0),
            MultiRead: Fill(value: 0),
            ZeroSearches: Fill(value: 0),
            CodeSessions: Fill(value: 3),
            SpecFirst: Fill(value: 0));
    }

    private static Expect NoData(string label) => new(label, ValueNone, StateWaiting, Waiting, TrendNone, HintNoData, NoGoal, StatsNone);

    private static Expect WaitingOneWeek(string label, string value) => new(label, value, StateWaiting, Waiting, TrendOneWeek, HintWait, NoGoal, StatsOneWeek);

    private static string Describe(MirrorTile tile)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"{tile.HistoryWeeks}|{Fixed(tile.CorridorLow)}|{Fixed(tile.CorridorHigh)}|{Fixed(tile.Median)}|{Fixed(tile.Mad)}");
    }

    private static string Fixed(double? value) => value is null ? "-" : value.Value.ToString("F9", CultureInfo.InvariantCulture);

    private static void AssertTiles(DashboardGold gold, params Expect[] expected)
    {
        IReadOnlyList<MirrorTile> tiles = gold.Mirror!.Tiles;
        Assert.Equal(expected.Length, tiles.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            MirrorTile actual = tiles[index];
            Assert.Equal(expected[index].Label, actual.Label);
            Assert.Equal(expected[index].Value, actual.Value);
            Assert.Equal(expected[index].State, actual.State);
            Assert.Equal(expected[index].Status, actual.Status);
            Assert.Equal(expected[index].Trend, actual.Trend);
            Assert.Equal(expected[index].Hint, actual.Hint);
            Assert.Equal(expected[index].Goal, actual.Goal);
            Assert.Equal(expected[index].Stats, Describe(actual));
        }
    }

    private static List<string> DescribeOrdering(SddPanelGold panel)
    {
        return [.. panel.Ordering.Select(row => string.Create(CultureInfo.InvariantCulture,
            $"{row.Week}|{row.Repo}|{row.CodeSessions}|{row.SpecFirstSessions}|{row.Rate:F4}")),];
    }

    private static List<string> DescribeWrites(SddPanelGold panel) => [.. panel.WritesByKind.Select(row => string.Create(CultureInfo.InvariantCulture, $"{row.Kind}|{row.Writes}"))];

    private static List<string> DescribeSkillRate(SddPanelGold panel)
    {
        return [.. panel.SkillRate.Select(row => string.Create(CultureInfo.InvariantCulture,
            $"{row.Repo}|{row.Sessions}|{row.SddSessions}|{row.Rate:F4}")),];
    }

    [Fact]
    public void Characterization_PracticeMirror_SteadyHistory_OkAcuteSickAndTrendUp()
    {
        DashboardGold gold = ComputeMirror(SteadyShape());

        AssertTiles(gold,
            new Expect(LabelCache, "78%", StateOk, "ok", "в коридоре 77%–78%", HintCache, NoGoal, StatsCache),
            new Expect(LabelBurner, "0%", StateOk, "ok", TrendFlatZero, HintBurner, NoGoal, StatsFlatZero),
            new Expect(LabelLoop, "75%", StateAcute, "acute", "острый выход: z = 4.4", HintAcute, "цель ≥30% · достигнута",
                "8|0.250000000|0.395833333|0.316666667|0.066666667"),
            new Expect(LabelSingleUse, "50%", "🔴", "sick", "в коридоре 66%–100%",
                "кандидаты на weeding — очередь предложений (хроника — кандидат в бэклог)", "цель ≤55% · достигнута",
                "8|0.659090909|1.000000000|0.791666667|0.181818182"),
            new Expect(LabelFailed, "30%", "📈", "trend", "наклон +0.9пп/нед", HintTrend, "цель ≤15% · до цели −15пп",
                "8|0.287500000|0.350000000|0.300000000|0.050000000"),
            new Expect(LabelSpec, "75%", StateOk, "ok", "в коридоре 75%–75%", "спека идёт перед кодом — норма", GoalSpecReached,
                StatsFlatThreeQuarters));
    }

    [Fact]
    public void Characterization_PracticeMirror_Breaks_AcuteSaturatedWaitAndTrendDown()
    {
        DashboardGold gold = ComputeMirror(BreakShape());

        AssertTiles(gold,
            new Expect(LabelCache, "68%", StateAcute, "acute", "острый выход: z = 32.7", HintAcute, NoGoal, StatsCache),
            new Expect(LabelBurner, "50%", StateAcute, "acute", "вне насыщенной нормы", HintAcute, NoGoal, StatsFlatZero),
            WaitingOneWeek(LabelLoop, "50%"),
            WaitingOneWeek(LabelSingleUse, "100%"),
            new Expect(LabelFailed, "0%", "📉", "trend", "наклон −10.0пп/нед", HintTrend, GoalFailedReached,
                "8|0.325000000|0.675000000|0.500000000|0.200000000"),
            NoData(LabelSpec));
    }

    [Fact]
    public void Characterization_PracticeMirror_CodeOnly_NoDataStableGoalReachedAndChronicSick()
    {
        DashboardGold gold = ComputeMirror(CodeOnlyShape());

        AssertTiles(gold,
            new Expect(LabelCache, "77%", StateOk, "ok", "в коридоре 77%–77%", HintCache, NoGoal,
                "8|0.773809524|0.773809524|0.773809524|0.000000000"),
            new Expect(LabelBurner, "0%", StateOk, "ok", TrendFlatZero, HintBurner, NoGoal, StatsFlatZero),
            NoData(LabelLoop),
            NoData(LabelSingleUse),
            new Expect(LabelFailed, "0%", StateOk, "ok", TrendFlatZero, "знание находится — норма", GoalFailedReached, StatsFlatZero),
            new Expect(LabelSpec, "0%", "🔴", "sick", TrendFlatZero,
                "сначала код — включи спека-скиллы в практику (хроника — кандидат в бэклог)", "цель ≥50% · до цели −50пп",
                StatsFlatZero));
    }

    [Fact]
    public void Characterization_PracticeMirror_NoEvents_AllSixTilesNoData()
    {
        DashboardGold gold = ComputeEmpty();

        AssertTiles(gold,
            NoData(LabelCache), NoData(LabelBurner), NoData(LabelLoop), NoData(LabelSingleUse), NoData(LabelFailed), NoData(LabelSpec));
    }

    [Fact]
    public void Characterization_PracticeMirror_SddFixture_ShortSpecHistoryAcuteSaturatedWithGoalGap()
    {
        DashboardGold gold = Compute(withSdd: true, new SddEventBuilder().Build());

        AssertTiles(gold,
            NoData(LabelCache),
            WaitingOneWeek(LabelBurner, "0%"),
            NoData(LabelLoop),
            NoData(LabelSingleUse),
            NoData(LabelFailed),
            new Expect(LabelSpec, "33%", StateAcute, "acute", "вне насыщенной нормы", HintAcute, "цель ≥50% · до цели −17пп",
                "7|0.000000000|0.000000000|0.000000000|0.000000000"));
    }

    [Fact]
    public void Characterization_SddPanel_FullData_EveryListAndSummary()
    {
        SddPanelGold panel = Compute(withSdd: true, new SddEventBuilder().Build()).SddPanel;

        Assert.Equal(6, panel.OrderingSummary.CodeSessions);
        Assert.Equal(2, panel.OrderingSummary.SpecFirstSessions);
        Assert.Equal(1.0 / 3, panel.OrderingSummary.Rate, precision: 9);
        Assert.Equal(
            [$"2026-W33|{RepoA}|3|1|0.3333", $"2026-W33|{RepoB}|1|0|0.0000", $"2026-W32|{RepoB}|1|1|1.0000", $"2026-W30|{Unknown}|1|0|0.0000"],
            DescribeOrdering(panel));
        Assert.Equal(["code|7", "config|2", "knowledge|2", "other|1"], DescribeWrites(panel));
        Assert.Equal(2, panel.MachineManagedWrites);
        Assert.True(panel.SkillConfigured);
        Assert.Equal([$"{RepoA}|3|1|0.3333", $"{Unknown}|2|1|0.5000", $"{RepoB}|2|1|0.5000"], DescribeSkillRate(panel));
    }

    [Fact]
    public void Characterization_SddPanel_NoPracticeData_EmptyListsButConfigured()
    {
        SddPanelGold panel = ComputeEmpty().SddPanel;

        Assert.Equal(0, panel.OrderingSummary.CodeSessions);
        Assert.Equal(0, panel.OrderingSummary.SpecFirstSessions);
        Assert.Equal(0.0, panel.OrderingSummary.Rate);
        Assert.Empty(panel.Ordering);
        Assert.Empty(panel.WritesByKind);
        Assert.Equal(0, panel.MachineManagedWrites);
        Assert.True(panel.SkillConfigured);
        Assert.Empty(panel.SkillRate);
    }
}
