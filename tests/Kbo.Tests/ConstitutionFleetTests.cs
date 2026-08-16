using Kbo.Gold;
using Kbo.Registry;

namespace Kbo.Tests;

public class ConstitutionFleetTests : IDisposable
{
    private readonly string workspace;
    private readonly string versionFile;
    private readonly string scanRoot;

    public ConstitutionFleetTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-fleet-tests").FullName;
        versionFile = Path.Combine(workspace, "VERSION");
        File.WriteAllText(versionFile, "15\n");
        scanRoot = Path.Combine(workspace, "repos");
        AddRepo("repo-current", """{"legislatorVersion": 15, "profiles": ["dotnet"]}""");
        AddRepo("repo-behind", """{"legislatorVersion": 14}""");
        AddRepo("repo-broken", "not json at all");
        Directory.CreateDirectory(Path.Combine(scanRoot, "not-legislated", "docs"));
        // Nested one level deeper than a scan root's direct children — out of scope.
        AddRepo(Path.Combine("nested", "deep-repo"), """{"legislatorVersion": 14}""");
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private void AddRepo(string name, string manifestJson)
    {
        string manifestDirectory = Path.Combine(scanRoot, name, "docs", "ai");
        Directory.CreateDirectory(manifestDirectory);
        File.WriteAllText(Path.Combine(manifestDirectory, "manifest.json"), manifestJson);
    }

    private ConstitutionConfig Config() => new(versionFile, [scanRoot]);

    [Fact]
    public void Scan_NullConfig_ReturnsNull()
    {
        Assert.Null(ConstitutionFleet.Scan(null));
    }

    [Fact]
    public void Scan_ReadsVersionsFromDirectChildManifests()
    {
        ConstitutionFleetGold gold = ConstitutionFleet.Scan(Config())!;

        Assert.Equal(15, gold.CurrentVersion);
        Assert.Equal(3, gold.Repos.Count);
        Assert.Equal(
            [Path.Combine(scanRoot, "repo-behind"), Path.Combine(scanRoot, "repo-broken"), Path.Combine(scanRoot, "repo-current")],
            gold.Repos.Select(repo => repo.Repo).ToList());
    }

    [Fact]
    public void Scan_MarksCurrentOkAndBehindRed()
    {
        ConstitutionFleetGold gold = ConstitutionFleet.Scan(Config())!;

        FleetRepoTile current = gold.Repos.Single(repo => repo.Repo.EndsWith("repo-current", StringComparison.Ordinal));
        FleetRepoTile behind = gold.Repos.Single(repo => repo.Repo.EndsWith("repo-behind", StringComparison.Ordinal));
        Assert.Equal(("15", "ok"), (current.Version, current.Status));
        Assert.Equal(("14", "red"), (behind.Version, behind.Status));
        Assert.Equal(2, gold.Behind);
    }

    [Fact]
    public void Scan_UnreadableManifest_IsRedWithUnknownVersion()
    {
        ConstitutionFleetGold gold = ConstitutionFleet.Scan(Config())!;

        FleetRepoTile broken = gold.Repos.Single(repo => repo.Repo.EndsWith("repo-broken", StringComparison.Ordinal));
        Assert.Equal(("?", "red"), (broken.Version, broken.Status));
    }

    [Fact]
    public void Scan_MissingScanRoot_IsSkipped()
    {
        ConstitutionConfig config = new(versionFile, [Path.Combine(workspace, "no-such-root"), scanRoot]);

        ConstitutionFleetGold gold = ConstitutionFleet.Scan(config)!;

        Assert.Equal(3, gold.Repos.Count);
    }

    [Fact]
    public void Scan_MissingVersionFile_Throws()
    {
        ConstitutionConfig config = new(Path.Combine(workspace, "no-such-VERSION"), [scanRoot]);

        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => ConstitutionFleet.Scan(config));

        Assert.Contains("versionFile", exception.Message);
    }

    [Fact]
    public void Scan_NonIntegerVersionFile_Throws()
    {
        string badVersionFile = Path.Combine(workspace, "VERSION-bad");
        File.WriteAllText(badVersionFile, "fifteen");
        ConstitutionConfig config = new(badVersionFile, [scanRoot]);

        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => ConstitutionFleet.Scan(config));

        Assert.Contains("integer", exception.Message);
    }
}
