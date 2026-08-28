namespace Kbo.Gold;

public enum MirrorDirection
{
    UpIsBetter,
    DownIsBetter,
}

/// <summary>The tile's declared target: a value and which side of it is healthy.</summary>
public sealed record MirrorGoal(double Value, MirrorDirection Direction);

/// <summary>Calibration verdict for one mirror tile (BL-033). State is the emoji the
/// renderer prints; StatusClass is the CSS class. Amber ("acute") is the only alarm.</summary>
public sealed record MirrorVerdict(
    string State,
    string StatusClass,
    double? CorridorLow,
    double? CorridorHigh,
    double? Median,
    double? Mad,
    double? SlopePerWeek,
    double? RobustZ,
    int HistoryWeeks);

/// <summary>Mirror calibration v1 (BL-033, design session 2026-08-27): state = emoji,
/// not color. A tile is judged against its own history (weekly snapshots): the p25–p75
/// corridor is "my normal", the robust z-score catches acute breaks, and the OLS slope
/// catches sustained drift. Chronic sickness (corridor outside the goal) is a backlog
/// item, not an alarm — only the acute ⚠️ may color the tile amber.</summary>
public static class MirrorCalibration
{
    public const int RequiredHistoryWeeks = 6;
    public const double AcuteZThreshold = 2.0;
    public const double SaturatedMadFloor = 1e-9;
    public const double SaturatedDeviationFloor = 0.02;
    public const double RobustSigmaFactor = 1.4826;
    public const double MinimumTrendSlopePerWeek = 0.005;

    public const string StateStableGood = "🟢";
    public const string StateStableSick = "🔴";
    public const string StateTrendUp = "📈";
    public const string StateTrendDown = "📉";
    public const string StateAcute = "⚠️";
    public const string StateWaiting = "⏳";

    public const string ClassOk = "ok";
    public const string ClassSick = "sick";
    public const string ClassTrend = "trend";
    public const string ClassAcute = "acute";
    public const string ClassWait = "wait";

    public static MirrorVerdict Evaluate(
        IReadOnlyList<double> history,
        double current,
        MirrorGoal? goal,
        bool trust)
    {
        int weeks = history.Count;
        if (weeks < RequiredHistoryWeeks)
        {
            return new MirrorVerdict(StateWaiting, ClassWait, null, null, null, null, null, null, weeks);
        }

        double median = Percentile(history, 0.5);
        double mad = MedianAbsoluteDeviation(history, median);
        double corridorLow = Percentile(history, 0.25);
        double corridorHigh = Percentile(history, 0.75);
        double slope = SlopePerWeek(history);

        double? robustZ = null;
        if (mad >= SaturatedMadFloor)
        {
            robustZ = Math.Abs(current - median) / (RobustSigmaFactor * mad);
        }

        bool acute = mad < SaturatedMadFloor
            ? Math.Abs(current - median) > SaturatedDeviationFloor
            : robustZ > AcuteZThreshold;
        if (acute)
        {
            return new MirrorVerdict(StateAcute, ClassAcute, corridorLow, corridorHigh, median, mad, slope, robustZ, weeks);
        }

        // Trust tiles suppress the trend state: a slow model-mix shift must not nag —
        // only an acute break alarms (design session 2026-08-27). The trend threshold
        // is 2×MAD of the weekly first differences: a single anomalous week inflates
        // neither it nor the corridor (robust by design).
        double[] weeklyDifferences = new double[weeks - 1];
        for (int index = 1; index < weeks; index++)
        {
            weeklyDifferences[index - 1] = history[index] - history[index - 1];
        }
        double trendMad = MedianAbsoluteDeviation(weeklyDifferences, Percentile(weeklyDifferences, 0.5));
        bool trend = !trust
            && Math.Abs(slope) > 2 * trendMad
            && Math.Abs(slope) > MinimumTrendSlopePerWeek;
        if (trend)
        {
            return new MirrorVerdict(
                slope > 0 ? StateTrendUp : StateTrendDown,
                ClassTrend, corridorLow, corridorHigh, median, mad, slope, robustZ, weeks);
        }

        bool corridorInsideGoal = goal is null
            || (goal.Direction == MirrorDirection.UpIsBetter ? corridorLow >= goal.Value : corridorHigh <= goal.Value);
        return corridorInsideGoal
            ? new MirrorVerdict(StateStableGood, ClassOk, corridorLow, corridorHigh, median, mad, slope, robustZ, weeks)
            : new MirrorVerdict(StateStableSick, ClassSick, corridorLow, corridorHigh, median, mad, slope, robustZ, weeks);
    }

    /// <summary>Linear-interpolated percentile (numpy default): position p·(n−1)
    /// between the two neighbouring order statistics.</summary>
    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        double[] sorted = [.. values.OrderBy(value => value)];
        double position = p * (sorted.Length - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        return lower == upper ? sorted[lower] : sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    public static double MedianAbsoluteDeviation(IReadOnlyList<double> values, double median)
    {
        double[] deviations = [.. values.Select(value => Math.Abs(value - median))];
        return Percentile(deviations, 0.5);
    }

    /// <summary>Ordinary least-squares slope of the weekly series, units per week.</summary>
    public static double SlopePerWeek(IReadOnlyList<double> weekly)
    {
        int count = weekly.Count;
        double meanX = (count - 1) / 2.0;
        double meanY = weekly.Average();
        double numerator = 0;
        double denominator = 0;
        for (int index = 0; index < count; index++)
        {
            double dx = index - meanX;
            numerator += dx * (weekly[index] - meanY);
            denominator += dx * dx;
        }
        return denominator == 0 ? 0 : numerator / denominator;
    }
}
