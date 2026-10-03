namespace Kbo.Gold;

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
