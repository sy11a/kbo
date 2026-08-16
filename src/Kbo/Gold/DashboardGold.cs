namespace Kbo.Gold;

public sealed record JobHealthTile(
    string Machine,
    string Agent,
    string Job,
    DateTimeOffset LastCompleted,
    double DaysSilent,
    string Status);

public sealed record LastSeenTile(
    string Machine,
    string Agent,
    DateTimeOffset LastEvent,
    double DaysSilent,
    string Status);

public sealed record ReadsByLayerRow(string Date, string Layer, long Reads);

public sealed record FailedSearchRow(string Date, long Searches, long ZeroHits, double Rate);

public sealed record KbTouchRow(string Date, long Sessions, long Touched, double Rate);

public sealed record TokensRow(string Date, long InputTokens, long CacheReadTokens);

public sealed record ThemeReadsRow(string Theme, string Source, long Reads, long Notes);

public sealed record RepoSessionsRow(string Repo, long Sessions, string Agents, DateTimeOffset LastStarted);

public sealed record ReuseRow(string Path, long Sessions, long Reads);

public sealed record ReuseSummary(long Notes, long SingleUse, double SingleUseRate);

public sealed record WriteReadRow(string Path, long LaterReads);

public sealed record WriteReadSummary(long Written, long Reused, double LoopRate);

/// <summary>One metric's this-week vs last-week values. Format is "percent" or "count".</summary>
public sealed record MetricDelta(string Label, double Current, double Previous, string Format, bool HigherIsBetter);

public sealed record RecentSessionRow(
    string Date,
    string Time,
    string Agent,
    string Repo,
    long Reads,
    long Searches,
    long Skills,
    long Writes,
    bool TouchedKb,
    long InputTokens,
    long CacheReadTokens);

public sealed record DashboardGold(
    DateTimeOffset GeneratedAt,
    string Machine,
    int DeadManThresholdDays,
    IReadOnlyList<JobHealthTile> JobHealth,
    IReadOnlyList<LastSeenTile> LastSeen,
    ConstitutionFleetGold? ConstitutionFleet,
    IReadOnlyList<ReadsByLayerRow> ReadsByLayerDaily,
    IReadOnlyList<FailedSearchRow> FailedSearchDaily,
    IReadOnlyList<KbTouchRow> KbTouchDaily,
    IReadOnlyList<TokensRow> TokensDaily,
    IReadOnlyList<ThemeReadsRow> ThemeReads,
    IReadOnlyList<ThemeReadsRow> UnusedThemes,
    IReadOnlyList<RepoSessionsRow> SessionsByRepo,
    IReadOnlyList<RecentSessionRow> RecentSessions,
    IReadOnlyList<DayCount> TopSkills,
    IReadOnlyList<DayCount> TopFailedSearches,
    IReadOnlyList<DayCount> ReadsByContentType,
    IReadOnlyList<ReuseRow> TopReusedNotes,
    ReuseSummary Reuse,
    IReadOnlyList<WriteReadRow> TopWriteReadNotes,
    WriteReadSummary WriteReadLoop,
    IReadOnlyList<MetricDelta> WeekOverWeek);
