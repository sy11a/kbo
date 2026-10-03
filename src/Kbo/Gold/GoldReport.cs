namespace Kbo.Gold;

internal sealed record DeadNote(
    string Path,
    string SourceId,
    string Layer,
    int DaysSinceModified,
    DateTimeOffset? LastRead,
    IReadOnlyList<string> SuggestedActions);

internal sealed record HotNote(
    string Path,
    string SourceId,
    long ReadsInWindow,
    long ReadsTotal,
    DateTimeOffset LastRead);

internal sealed record StaleNote(
    string Path,
    string SourceId,
    long ReadsInWindow,
    int DaysSinceModified);

internal sealed record DormantSource(
    string SourceId,
    DateTimeOffset? LastActivity,
    int WithheldDeadNotes);

internal sealed record GoldReport(
    DateTimeOffset GeneratedAt,
    string Machine,
    int MinInventoryAgeDays,
    int ReadWindowDays,
    int StaleMinReads,
    int StaleUnmodifiedDays,
    int DormantAfterDays,
    IReadOnlyDictionary<string, int> InventoryCounts,
    IReadOnlyDictionary<string, int> LifecycleCounts,
    IReadOnlyDictionary<string, int> MachineManagedCounts,
    IReadOnlyList<DormantSource> DormantSources,
    IReadOnlyList<DeadNote> DeadNotes,
    IReadOnlyList<HotNote> HotNotes,
    IReadOnlyList<StaleNote> StaleNotes);
