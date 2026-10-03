namespace Kbo.Gold;

internal sealed record DaySession(
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
