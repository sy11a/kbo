namespace Kbo.Gold;

internal sealed record StaleNote(
    string Path,
    string SourceId,
    long ReadsInWindow,
    int DaysSinceModified);
