namespace Kbo.Gold;

public sealed record DayCount(string Label, long Count);

public sealed record DaySession(
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

/// <summary>
/// One day's activity digest, computed once from silver (P2). Knowledge is
/// classified registry-now (ADR-0021): subjects resolved through the current
/// registry, not the capture-time kbroot stamp.
/// </summary>
public sealed record DayDigest(
    string Date,
    long Sessions,
    long SessionsTouchingKb,
    double KbTouchRate,
    IReadOnlyList<DayCount> ByAgent,
    IReadOnlyList<DayCount> ByRepo,
    IReadOnlyList<DayCount> ReadsByLayer,
    long TotalReads,
    long Searches,
    long SearchHits,
    long SearchZeroHits,
    IReadOnlyList<DayCount> TopZeroHitQueries,
    long InputTokens,
    long CacheReadTokens,
    IReadOnlyList<DayCount> SkillsUsed,
    IReadOnlyList<DaySession> SessionDetail);
