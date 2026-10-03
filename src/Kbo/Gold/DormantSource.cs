namespace Kbo.Gold;

internal sealed record DormantSource(
    string SourceId,
    DateTimeOffset? LastActivity,
    int WithheldDeadNotes);
