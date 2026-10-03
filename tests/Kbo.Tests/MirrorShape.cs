namespace Kbo.Tests;

/// <summary>
/// Per-week shape (11 weeks, Mondays 2026-06-01..2026-08-10) of the practice-mirror fixtures.
/// </summary>
internal sealed record MirrorShape(
    int[] CacheA,
    int[] Written,
    int[] Reused,
    int[] MultiRead,
    int[] ZeroSearches,
    int[] CodeSessions,
    int[] SpecFirst);
