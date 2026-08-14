namespace Kbo.Gold;

public sealed record MissingSessionsFinding(
    string Agent,
    string Machine,
    int Count,
    DateTimeOffset MissingSince,
    IReadOnlyList<string> Transcripts);

public sealed record UnregisteredSourceFinding(string Directory, long ReadCount);

public sealed record AuditReport(
    DateTimeOffset GeneratedAt,
    string Machine,
    IReadOnlyList<string> AgentsWithoutSessionAudit,
    IReadOnlyList<MissingSessionsFinding> MissingSessions,
    IReadOnlyList<UnregisteredSourceFinding> UnregisteredSources);
