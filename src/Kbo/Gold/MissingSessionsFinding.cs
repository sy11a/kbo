namespace Kbo.Gold;

internal sealed record MissingSessionsFinding(
    string Agent,
    string Machine,
    int Count,
    DateTimeOffset MissingSince,
    IReadOnlyList<string> Transcripts);
