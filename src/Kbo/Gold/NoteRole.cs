namespace Kbo.Gold;

/// <summary>
/// Classifies a note by its death condition: reference notes die by
/// non-use and belong on the dead worklist; lifecycle artifacts
/// (executed plans/specs, dated journals) are done when their work is
/// done and never belong there; machine-managed files (fleet law under
/// docs/ai/, ADR scaffolding templates) are overwritten by tooling and
/// were never knowledge to prune. Path-segment-based; pure, no I/O.
/// Mirror of ContentKind (ADR-0025 pattern).
/// </summary>
public static class NoteRole
{
    public const string Reference = "reference";
    public const string Lifecycle = "lifecycle";
    public const string MachineManaged = "machine-managed";

    private static readonly string[] LifecycleSegments =
    [
        "/superpowers/plans/",
        "/superpowers/specs/",
        "/journal/",
    ];

    private static readonly string[] MachineManagedSegments =
    [
        "/docs/ai/",
    ];

    private static readonly string[] MachineManagedSuffixes =
    [
        "/adr/template.md",
    ];

    public static string Of(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (MachineManagedSegments.Any(segment => normalized.Contains(segment, StringComparison.Ordinal))
            || MachineManagedSuffixes.Any(suffix => normalized.EndsWith(suffix, StringComparison.Ordinal)))
        {
            return MachineManaged;
        }
        return LifecycleSegments.Any(segment => normalized.Contains(segment, StringComparison.Ordinal))
            ? Lifecycle
            : Reference;
    }
}
