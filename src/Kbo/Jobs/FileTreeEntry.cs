namespace Kbo.Jobs;

internal sealed record FileTreeEntry(string Root, string Pattern, string DestinationPrefix) : ArchiveEntry;
