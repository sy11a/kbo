namespace Kbo.Jobs;

/// <summary>
/// Cadence-aware dead-man thresholds (ADR-0037): a job is silent only when
/// past its cadence's due point plus the same 3-day grace every cadence
/// gets. Weekly jobs are due at 6.5 days (PulseRunner), so red starts at
/// 9.5 — a healthy weekly job is never flagged. The weekly set must match
/// the PulseCommand registrations; PulseCommand resolves its cadences from
/// here so the two cannot diverge.
/// </summary>
public static class JobDeadMan
{
    public const double GraceDays = 3;
    public const double DailyThresholdDays = GraceDays;
    public const double WeeklyThresholdDays = PulseRunner.WeeklyDueDays + GraceDays;

    private static readonly HashSet<string> WeeklyJobs = ["report", "audit"];

    public static JobCadence CadenceOf(string jobName)
    {
        return WeeklyJobs.Contains(jobName) ? JobCadence.Weekly : JobCadence.Daily;
    }

    public static double ThresholdDays(string jobName)
    {
        return CadenceOf(jobName) == JobCadence.Weekly ? WeeklyThresholdDays : DailyThresholdDays;
    }
}
