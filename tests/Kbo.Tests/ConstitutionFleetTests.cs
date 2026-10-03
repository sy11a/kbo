using Kbo.Gold;
using Kbo.Registry;

namespace Kbo.Tests;

public sealed class ConstitutionFleetTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _versionFile;
    private readonly string _scanRoot;

    public ConstitutionFleetTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-fleet-tests").FullName;
        _versionFile = Path.Combine(_workspace, "VERSION");
        File.WriteAllText(_versionFile, "15\n");
        _scanRoot = Path.Combine(_workspace, "repos");
        AddRepo("repo-current", /*lang=json,strict*/ """{"legislatorVersion": 15, "profiles": ["dotnet"]}""");
        AddRepo("repo-behind", /*lang=json,strict*/ """{"legislatorVersion": 14}""");
        AddRepo("repo-broken", "not json at all");
        _ = Directory.CreateDirectory(Path.Combine(_scanRoot, "not-legislated", "docs"));
        // Nested one level deeper than a scan root's direct children — out of scope.
        AddRepo(Path.Combine("nested", "deep-repo"), /*lang=json,strict*/ """{"legislatorVersion": 14}""");
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private void AddRepo(string name, string manifestJson)
    {
        string manifestDirectory = Path.Combine(_scanRoot, name, "docs", "ai");
        _ = Directory.CreateDirectory(manifestDirectory);
        File.WriteAllText(Path.Combine(manifestDirectory, "manifest.json"), manifestJson);
    }

    private ConstitutionConfig Config() => new(_versionFile, [_scanRoot]);

    [Fact]
    public void Scan_NullConfig_ReturnsNull() => Assert.Null(ConstitutionFleet.Scan(config: null));

    [Fact]
    public void Scan_ReadsVersionsFromDirectChildManifests()
    {
        ConstitutionFleetGold gold = ConstitutionFleet.Scan(Config())!;

        Assert.Equal(15, gold.CurrentVersion);
        Assert.Equal(3, gold.Repos.Count);
        Assert.Equal(
            [Path.Combine(_scanRoot, "repo-behind"), Path.Combine(_scanRoot, "repo-broken"), Path.Combine(_scanRoot, "repo-current")],
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
    public void Scan_ExcludedRepoName_IsNotScanned()
    {
        ConstitutionConfig config = new(_versionFile, [_scanRoot]) { Exclude = ["repo-behind"] };

        ConstitutionFleetGold gold = ConstitutionFleet.Scan(config)!;

        Assert.DoesNotContain(gold.Repos, repo => repo.Repo.EndsWith("repo-behind", StringComparison.Ordinal));
        Assert.Equal(2, gold.Repos.Count);
        Assert.Equal(1, gold.Behind);
    }

    [Fact]
    public void Scan_MissingScanRoot_IsSkipped()
    {
        ConstitutionConfig config = new(_versionFile, [Path.Combine(_workspace, "no-such-root"), _scanRoot]);

        ConstitutionFleetGold gold = ConstitutionFleet.Scan(config)!;

        Assert.Equal(3, gold.Repos.Count);
    }

    [Fact]
    public void Scan_MissingVersionFile_Throws()
    {
        ConstitutionConfig config = new(Path.Combine(_workspace, "no-such-VERSION"), [_scanRoot]);

        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => ConstitutionFleet.Scan(config));

        Assert.Contains("versionFile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_NonIntegerVersionFile_Throws()
    {
        string badVersionFile = Path.Combine(_workspace, "VERSION-bad");
        File.WriteAllText(badVersionFile, "fifteen");
        ConstitutionConfig config = new(badVersionFile, [_scanRoot]);

        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => ConstitutionFleet.Scan(config));

        Assert.Contains("integer", exception.Message, StringComparison.Ordinal);
    }
}
