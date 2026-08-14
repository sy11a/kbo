namespace Kbo.Jobs;

/// <summary>
/// Adapter contract #3: where an agent's transcripts/sessions live on disk.
/// The archive job and the completeness audit iterate manifests, never hardcoded paths.
/// </summary>
public sealed record RetentionManifest(
    string Agent,
    IReadOnlyList<ArchiveEntry> Entries,
    FileTreeEntry? SessionFiles = null,
    SqliteSessionSource? SessionDatabase = null);

/// <summary>
/// Session enumeration for agents whose sessions live in a SQLite store.
/// The query must return (id TEXT, modified_ms INTEGER).
/// </summary>
public sealed record SqliteSessionSource(string DatabasePath, string IdQuery);

public abstract record ArchiveEntry;

public sealed record FileTreeEntry(string Root, string Pattern, string DestinationPrefix) : ArchiveEntry;

public sealed record SingleFileEntry(string Path, string Destination) : ArchiveEntry;

public sealed record SqliteEntry(
    string DatabasePath,
    string DestinationPrefix,
    string LatestFileName,
    string WeeklySnapshotPrefix) : ArchiveEntry;
