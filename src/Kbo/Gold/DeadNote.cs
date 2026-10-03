namespace Kbo.Gold;

internal sealed record DeadNote(
    string Path,
    string SourceId,
    string Layer,
    int DaysSinceModified,
    DateTimeOffset? LastRead,
    IReadOnlyList<string> SuggestedActions);
