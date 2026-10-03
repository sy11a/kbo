namespace Kbo.Gold;

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
