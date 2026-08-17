using System.Globalization;
using System.Text.Json;
using Kbo.Registry;

namespace Kbo.Gold;

public sealed record FleetRepoTile(string Repo, string Version, string Status);

public sealed record ConstitutionFleetGold(int CurrentVersion, IReadOnlyList<FleetRepoTile> Repos, int Behind);

/// <summary>
/// Legislated-repo fleet vs the current constitution version. There is no
/// fleet registry to maintain: the repos' docs/ai/manifest.json files ARE the
/// database (derived-rebuildable) — this scans the configured roots' direct
/// children at report time (ADR-0038). An unreadable manifest renders as
/// version "?" and counts as behind: unknown classification fails toward the
/// cheap error.
/// </summary>
public static class ConstitutionFleet
{
    public static ConstitutionFleetGold? Scan(ConstitutionConfig? config)
    {
        if (config is null)
        {
            return null;
        }
        if (!File.Exists(config.VersionFile))
        {
            throw new RegistryFormatException($"constitution versionFile not found: {config.VersionFile}");
        }
        if (!int.TryParse(File.ReadAllText(config.VersionFile).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int currentVersion))
        {
            throw new RegistryFormatException($"constitution versionFile is not a bare integer: {config.VersionFile}");
        }

        string current = currentVersion.ToString(CultureInfo.InvariantCulture);
        List<FleetRepoTile> repos = new();
        foreach (string root in config.ScanRoots.Where(Directory.Exists))
        {
            foreach (string repo in Directory.EnumerateDirectories(root))
            {
                if (config.Exclude.Contains(Path.GetFileName(repo), StringComparer.Ordinal))
                {
                    continue;
                }
                string manifest = Path.Combine(repo, "docs", "ai", "manifest.json");
                if (!File.Exists(manifest))
                {
                    continue;
                }
                string version = ReadLegislatorVersion(manifest);
                repos.Add(new FleetRepoTile(repo, version, version == current ? "ok" : "red"));
            }
        }
        repos.Sort((a, b) => string.CompareOrdinal(a.Repo, b.Repo));
        return new ConstitutionFleetGold(currentVersion, repos, repos.Count(repo => repo.Status == "red"));
    }

    private static string ReadLegislatorVersion(string manifestPath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("legislatorVersion", out JsonElement value)
                && value.TryGetInt32(out int version))
            {
                return version.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch (JsonException)
        {
        }
        return "?";
    }
}
