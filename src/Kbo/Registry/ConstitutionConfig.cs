namespace Kbo.Registry;

/// <summary>
/// Optional legislator wiring: the constitution's VERSION file and the roots
/// whose direct children are candidate legislated repos. Null when the
/// registry has no constitution block — a public tool ships no default
/// legislator location (ADR-0031 pattern).
/// </summary>
public sealed record ConstitutionConfig(string VersionFile, IReadOnlyList<string> ScanRoots)
{
    /// <summary>
    /// Directory basenames skipped by the fleet scan — e.g. an archived repo
    /// that still carries a manifest but is deliberately never upgraded.
    /// </summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];
}
