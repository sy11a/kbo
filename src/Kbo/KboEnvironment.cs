namespace Kbo;

/// <summary>
/// Environment variables and default locations shared across kbo commands
/// (ADR-0005 registry overlay, ADR-0006 events repo).
/// </summary>
public static class KboEnvironment
{
    public const string RegistryVariable = "KBO_REGISTRY";
    public const string EventsRepoVariable = "KBO_EVENTS_REPO";
    public const string SilverVariable = "KBO_SILVER";
    public const string ArchiveRootVariable = "KB_ARCHIVE_ROOT";
    public const string ResticRepoVariable = "KB_RESTIC_REPO";

    public static string DefaultEventsRepo(string homeDirectory)
    {
        return Path.Combine(homeDirectory, "Repository", "kb-events");
    }

    public static string DefaultSilverPath(string homeDirectory)
    {
        return Path.Combine(homeDirectory, ".local", "share", "kbo", "silver.duckdb");
    }
}
