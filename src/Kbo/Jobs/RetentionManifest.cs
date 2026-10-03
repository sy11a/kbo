namespace Kbo.Jobs;

/// <summary>
/// Adapter contract #3: where an agent's transcripts/sessions live on disk.
/// The archive job and the completeness audit iterate manifests, never hardcoded paths.
/// </summary>
internal sealed record RetentionManifest(
    string Agent,
    IReadOnlyList<ArchiveEntry> Entries,
    FileTreeEntry? SessionFiles = null,
    SqliteSessionSource? SessionDatabase = null);
