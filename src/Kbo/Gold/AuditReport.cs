namespace Kbo.Gold;

internal sealed record AuditReport(
    DateTimeOffset GeneratedAt,
    string Machine,
    IReadOnlyList<string> AgentsWithoutSessionAudit,
    IReadOnlyList<MissingSessionsFinding> MissingSessions,
    IReadOnlyList<UnregisteredSourceFinding> UnregisteredSources);
