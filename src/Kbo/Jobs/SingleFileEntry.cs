namespace Kbo.Jobs;

internal sealed record SingleFileEntry(string Path, string Destination) : ArchiveEntry;
