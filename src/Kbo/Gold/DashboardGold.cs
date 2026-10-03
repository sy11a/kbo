namespace Kbo.Gold;

internal sealed record DashboardGold(
    DateTimeOffset GeneratedAt,
    string Machine,
    int DeadManThresholdDays,
    double WeeklyDeadManThresholdDays,
    IReadOnlyList<JobHealthTile> JobHealth,
    IReadOnlyList<LastSeenTile> LastSeen,
    ConstitutionFleetGold? ConstitutionFleet,
    ServiceSessionsSummary ServiceSessions,
    SddPanelGold SddPanel,
    IReadOnlyList<FailedSearchRow> FailedSearchDaily,
    IReadOnlyList<TokensRow> TokensDaily,
    IReadOnlyList<ThemeReadsRow> UnusedThemes,
    IReadOnlyList<RecentSessionRow> RecentSessions,
    IReadOnlyList<DayCount> TopFailedSearches,
    IReadOnlyList<ReuseRow> TopReusedNotes,
    ReuseSummary Reuse,
    IReadOnlyList<WriteReadRow> TopWriteReadNotes,
    WriteReadSummary WriteReadLoop,
    PracticeMirrorGold? Mirror = null);
