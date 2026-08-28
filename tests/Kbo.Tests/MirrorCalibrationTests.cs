using Kbo.Gold;

namespace Kbo.Tests;

public class MirrorCalibrationTests
{
    private static readonly MirrorGoal DownGoal = new(0.15, MirrorDirection.DownIsBetter);
    private static readonly MirrorGoal UpGoal = new(0.30, MirrorDirection.UpIsBetter);

    [Fact]
    public void Percentile_LinearInterpolationBetweenOrderStatistics()
    {
        Assert.Equal(2, MirrorCalibration.Percentile(new double[] { 1, 2, 3, 4, 5 }, 0.25));
        Assert.Equal(2.5, MirrorCalibration.Percentile(new double[] { 1, 2, 3, 4 }, 0.5));
        Assert.Equal(1, MirrorCalibration.Percentile(new double[] { 1, 5 }, 0));
        Assert.Equal(5, MirrorCalibration.Percentile(new double[] { 5, 1 }, 1));
    }

    [Fact]
    public void SlopePerWeek_FitsDirectionAndFlatSeries()
    {
        Assert.Equal(1, MirrorCalibration.SlopePerWeek(new double[] { 0, 1, 2, 3 }), 5);
        Assert.Equal(-1, MirrorCalibration.SlopePerWeek(new double[] { 4, 3, 2, 1 }), 5);
        Assert.Equal(0, MirrorCalibration.SlopePerWeek(new double[] { 2, 2, 2, 2, 2 }), 5);
        Assert.Equal(0.02, MirrorCalibration.SlopePerWeek(new double[] { 0.10, 0.12, 0.14, 0.16, 0.18, 0.20, 0.22 }), 5);
    }

    [Fact]
    public void Evaluate_TooFewHistoryWeeks_WaitsWithoutState()
    {
        MirrorVerdict verdict = MirrorCalibration.Evaluate(
            new double[] { 0.1, 0.2, 0.15, 0.12, 0.18 }, 0.2, DownGoal, trust: false);

        Assert.Equal(MirrorCalibration.StateWaiting, verdict.State);
        Assert.Equal(MirrorCalibration.ClassWait, verdict.StatusClass);
        Assert.Equal(5, verdict.HistoryWeeks);
        Assert.Null(verdict.CorridorLow);
    }

    [Fact]
    public void Evaluate_CorridorInsideGoal_StableGood()
    {
        MirrorVerdict verdict = MirrorCalibration.Evaluate(
            new double[] { 0.10, 0.14, 0.11, 0.13, 0.12, 0.14, 0.11 }, 0.12, DownGoal, trust: false);

        Assert.Equal(MirrorCalibration.StateStableGood, verdict.State);
        Assert.Equal(MirrorCalibration.ClassOk, verdict.StatusClass);
        Assert.Equal(0.135, verdict.CorridorHigh!.Value, 4);
        Assert.Equal(0.11, verdict.CorridorLow!.Value, 4);
    }

    [Fact]
    public void Evaluate_CorridorOutsideGoal_StableSickEvenWhenValueInsideCorridor()
    {
        // The hurting-case shape: oscillating failed-search history stuck far from
        // the goal (design session 2026-08-27).
        MirrorVerdict verdict = MirrorCalibration.Evaluate(
            new double[] { 0.198, 0.318, 0.217, 0.270, 0.237, 0.366, 0.270 }, 0.28, DownGoal, trust: false);

        Assert.Equal(MirrorCalibration.StateStableSick, verdict.State);
        Assert.Equal(MirrorCalibration.ClassSick, verdict.StatusClass);
        Assert.NotEqual(MirrorCalibration.ClassAcute, verdict.StatusClass);
        Assert.Equal(0.227, verdict.CorridorLow!.Value, 4);
        Assert.Equal(0.294, verdict.CorridorHigh!.Value, 4);
        Assert.True(verdict.RobustZ is null || verdict.RobustZ <= MirrorCalibration.AcuteZThreshold);
    }

    [Fact]
    public void Evaluate_CorridorStraddlesGoal_StableSick()
    {
        // p25 below the goal, p75 above: "неустойчиво у цели" — not stable-good
        // (operator clarification 2026-08-28).
        MirrorVerdict verdict = MirrorCalibration.Evaluate(
            new double[] { 0.11, 0.15, 0.12, 0.16, 0.13, 0.17, 0.12 }, 0.15, DownGoal, trust: false);

        Assert.Equal(MirrorCalibration.StateStableSick, verdict.State);
    }

    [Fact]
    public void Evaluate_UpGoalUsesCorridorLow()
    {
        MirrorVerdict verdict = MirrorCalibration.Evaluate(
            new double[] { 0.35, 0.41, 0.36, 0.40, 0.37, 0.42, 0.38 }, 0.38, UpGoal, trust: false);

        Assert.Equal(MirrorCalibration.StateStableGood, verdict.State);

        MirrorVerdict sick = MirrorCalibration.Evaluate(
            new double[] { 0.15, 0.21, 0.16, 0.20, 0.17, 0.22, 0.18 }, 0.18, UpGoal, trust: false);
        Assert.Equal(MirrorCalibration.StateStableSick, sick.State);
    }

    [Fact]
    public void Evaluate_AcuteBreakBeyondRobustZ_Acute()
    {
        MirrorVerdict verdict = MirrorCalibration.Evaluate(
            new double[] { 0.10, 0.11, 0.12, 0.12, 0.13, 0.13, 0.14 }, 0.20, DownGoal, trust: false);

        Assert.Equal(MirrorCalibration.StateAcute, verdict.State);
        Assert.Equal(MirrorCalibration.ClassAcute, verdict.StatusClass);
        Assert.True(verdict.RobustZ > MirrorCalibration.AcuteZThreshold);
    }

    [Fact]
    public void Evaluate_SaturatedSeries_UsesAbsoluteFloor()
    {
        double[] saturated = { 0, 0, 0, 0, 0, 0, 0 };

        MirrorVerdict jolt = MirrorCalibration.Evaluate(saturated, 0.05, DownGoal, trust: true);
        Assert.Equal(MirrorCalibration.StateAcute, jolt.State);

        MirrorVerdict calm = MirrorCalibration.Evaluate(saturated, 0.01, DownGoal, trust: true);
        Assert.Equal(MirrorCalibration.StateStableGood, calm.State);
        Assert.Null(calm.RobustZ);
    }

    [Fact]
    public void Evaluate_TrustTile_SuppressesTrendWithoutGoal()
    {
        // A steady ramp that would flag 📈 on a goal-bearing tile stays 🟢 on a
        // trust tile — only an acute break may alarm (design session 2026-08-27).
        double[] ramp = { 0.90, 0.91, 0.92, 0.93, 0.94, 0.95, 0.96 };

        MirrorVerdict trust = MirrorCalibration.Evaluate(ramp, 0.96, goal: null, trust: true);
        Assert.Equal(MirrorCalibration.StateStableGood, trust.State);

        MirrorVerdict goalBearing = MirrorCalibration.Evaluate(ramp, 0.96, UpGoal, trust: false);
        Assert.Equal(MirrorCalibration.StateTrendUp, goalBearing.State);
        Assert.Equal(MirrorCalibration.ClassTrend, goalBearing.StatusClass);
    }

    [Fact]
    public void Evaluate_NoisyDriftBelowTrendMad_NoTrendFlag()
    {
        // Oscillation around a flat mean: the spike inflates the robust diffs-MAD,
        // not the verdict — noise, not drift; the tile answers the goal question.
        MirrorVerdict verdict = MirrorCalibration.Evaluate(
            new double[] { 0.198, 0.318, 0.217, 0.270, 0.237, 0.366, 0.270 }, 0.28, DownGoal, trust: false);

        Assert.NotEqual(MirrorCalibration.ClassTrend, verdict.StatusClass);
    }

    [Fact]
    public void Evaluate_TrendDown_FlaggedOnFallingSeries()
    {
        MirrorVerdict verdict = MirrorCalibration.Evaluate(
            new double[] { 0.50, 0.45, 0.40, 0.35, 0.30, 0.25, 0.20 }, 0.20, UpGoal, trust: false);

        Assert.Equal(MirrorCalibration.StateTrendDown, verdict.State);
        Assert.True(verdict.SlopePerWeek < 0);
    }
}
