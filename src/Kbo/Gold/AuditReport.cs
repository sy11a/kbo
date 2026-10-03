namespace Kbo.Gold;

internal sealed record MissingSessionsFinding(
    string Agent,
    string Machine,
    int Count,
    DateTimeOffset MissingSince,
    IReadOnlyList<string> Transcripts);

internal sealed record UnregisteredSourceFinding(string Directory, long ReadCount);

internal sealed record AuditReport(
    DateTimeOffset GeneratedAt,
    string Machine,
    IReadOnlyList<string> AgentsWithoutSessionAudit,
    IReadOnlyList<MissingSessionsFinding> MissingSessions,
    IReadOnlyList<UnregisteredSourceFinding> UnregisteredSources);
