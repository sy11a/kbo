namespace Kbo.Jobs;

internal sealed record SqliteEntry(
    string DatabasePath,
    string DestinationPrefix,
    string LatestFileName,
    string WeeklySnapshotPrefix) : ArchiveEntry;
