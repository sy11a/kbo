namespace Kbo.Gold;

/// <summary>
/// Classifies a note by its death condition: reference notes die by
/// non-use and belong on the dead worklist; lifecycle artifacts
/// (executed plans/specs, dated journals) are done when their work is
/// done and never belong there. Path-segment-based; pure, no I/O.
/// Mirror of ContentKind (ADR-0025 pattern).
/// </summary>
public static class NoteRole
{
    public const string Reference = "reference";
    public const string Lifecycle = "lifecycle";

    private static readonly string[] LifecycleSegments =
    [
        "/superpowers/plans/",
        "/superpowers/specs/",
        "/journal/",
    ];

    public static string Of(string path)
    {
        string normalized = path.Replace('\\', '/');
        return LifecycleSegments.Any(segment => normalized.Contains(segment, StringComparison.Ordinal))
            ? Lifecycle
            : Reference;
    }
}
