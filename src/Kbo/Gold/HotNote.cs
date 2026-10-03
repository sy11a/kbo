namespace Kbo.Gold;

internal sealed record HotNote(
    string Path,
    string SourceId,
    long ReadsInWindow,
    long ReadsTotal,
    DateTimeOffset LastRead);
