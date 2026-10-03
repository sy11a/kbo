namespace Kbo.Gold;

internal sealed record JobHealthTile(
    string Machine,
    string Agent,
    string Job,
    DateTimeOffset LastCompleted,
    double DaysSilent,
    string Status);

internal sealed record LastSeenTile(
    string Machine,
    string Agent,
    DateTimeOffset LastEvent,
    double DaysSilent,
    string Status);

internal sealed record ServiceSessionsSummary(long Sessions, string Agents);

internal sealed record FailedSearchRow(string Date, long Searches, long ZeroHits, double Rate);

internal sealed record TokensRow(string Date, long InputTokens, long CacheReadTokens);

internal sealed record ThemeReadsRow(string Theme, string Source, long Reads, long Notes);

internal sealed record ReuseRow(string Path, long Sessions, long Reads);

internal sealed record ReuseSummary(long Notes, long SingleUse, double SingleUseRate);

internal sealed record WriteReadRow(string Path, long LaterReads);

internal sealed record WriteReadSummary(long Written, long Reused, double LoopRate);

internal sealed record SddOrderingRow(string Week, string Repo, long CodeSessions, long SpecFirstSessions, double Rate);

internal sealed record SddOrderingSummary(long CodeSessions, long SpecFirstSessions, double Rate);

internal sealed record SddWritesRow(string Kind, long Writes);

internal sealed record SddSkillRateRow(string Repo, long Sessions, long SddSessions, double Rate);

/// <summary>SDD-practice panel (ADR-0040): spec-before-code ordering,
/// writes by content kind (machine-managed excluded and disclosed —
/// no-silent-caps), SDD-skill rate (empty + <paramref name="SkillConfigured"/>
/// false when the registry has no sdd block).</summary>
internal sealed record SddPanelGold(
    IReadOnlyList<SddOrderingRow> Ordering,
    SddOrderingSummary OrderingSummary,
    IReadOnlyList<SddWritesRow> WritesByKind,
    long MachineManagedWrites,
    IReadOnlyList<SddSkillRateRow> SkillRate,
    bool SkillConfigured);

/// <summary>Practice mirror (2026-08-27 design session, calibrated in BL-033): the
/// first screen — six tiles, each judged against its own history corridor and a goal.
/// <paramref name="State"/> is the emoji, <paramref name="StatusClass"/> the CSS class
/// (ok | sick | trend | acute | wait — "acute" is the only amber). <paramref name="Goal"/>
/// is the pre-rendered goal line; corridors and thresholds live here, never in the
/// renderer (zero computation, P2).</summary>
internal sealed record MirrorTile(
    string Label,
    string Value,
    string Trend,
    string Hint,
    string Status,
    string State = "",
    string? Goal = null,
    double? CorridorLow = null,
    double? CorridorHigh = null,
    double? Median = null,
    double? Mad = null,
    int HistoryWeeks = 0);

internal sealed record PracticeMirrorGold(IReadOnlyList<MirrorTile> Tiles);

internal sealed record RecentSessionRow(
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
