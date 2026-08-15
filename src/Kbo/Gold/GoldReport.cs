namespace Kbo.Gold;

public sealed record DeadNote(
    string Path,
    string SourceId,
    string Layer,
    int DaysSinceModified,
    DateTimeOffset? LastRead,
    IReadOnlyList<string> SuggestedActions);

public sealed record HotNote(
    string Path,
    string SourceId,
    long ReadsInWindow,
    long ReadsTotal,
    DateTimeOffset LastRead);

public sealed record StaleNote(
    string Path,
    string SourceId,
    long ReadsInWindow,
    int DaysSinceModified);

public sealed record GoldReport(
    DateTimeOffset GeneratedAt,
    string Machine,
    int MinInventoryAgeDays,
    int ReadWindowDays,
    int StaleMinReads,
    int StaleUnmodifiedDays,
    IReadOnlyDictionary<string, int> InventoryCounts,
    IReadOnlyDictionary<string, int> LifecycleCounts,
    IReadOnlyList<DeadNote> DeadNotes,
    IReadOnlyList<HotNote> HotNotes,
    IReadOnlyList<StaleNote> StaleNotes);
