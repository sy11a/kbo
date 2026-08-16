using System.Text.Json;
using Kbo.Gold;
using Kbo.Registry;

namespace Kbo.Cli;

public static class ReportCommand
{
    private const string Usage = "usage: kbo report [--out <dir>]";

    private static readonly JsonSerializerOptions GoldJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string, string?> environment,
        string homeDirectory)
    {
        string? explicitOut = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--out" && index + 1 < args.Length)
            {
                explicitOut = args[++index];
            }
            else
            {
                error.WriteLine(Usage);
                return 1;
            }
        }

        KnowledgeRegistry registry;
        try
        {
            registry = KnowledgeRegistry.Load(
                RegistryLocator.Locate(null, environment, homeDirectory),
                environment(KboEnvironment.TaskPatternVariable));
        }
        catch (RegistryFormatException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }

        KnowledgeSource? vault = registry.Sources.FirstOrDefault(source => source.Layer == KnowledgeLayer.Global);
        if (vault is null)
        {
            error.WriteLine("registry has no global-layer source (the vault); cannot locate _generated/");
            return 1;
        }

        string silverPath = environment(KboEnvironment.SilverVariable)
            ?? KboEnvironment.DefaultSilverPath(homeDirectory);
        if (!File.Exists(silverPath))
        {
            error.WriteLine($"silver not found at {silverPath} — run 'kbo rebuild' first");
            return 1;
        }

        ConstitutionFleetGold? fleet;
        try
        {
            fleet = ConstitutionFleet.Scan(registry.Constitution);
        }
        catch (RegistryFormatException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }

        GoldReport report = GoldComputer.Compute(silverPath, registry, TimeProvider.System);
        DashboardGold dashboard = DashboardComputer.Compute(silverPath, registry, TimeProvider.System, fleet);
        IReadOnlyList<DayDigest> digests = DailyDigestComputer.Compute(silverPath, registry, TimeProvider.System);

        string outputDirectory = explicitOut ?? Path.Combine(vault.Root, "_generated");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(
            Path.Combine(outputDirectory, "README.md"),
            "# GENERATED — do not edit\n\nEverything in this folder is written by `kbo report` and overwritten on every run.\n");
        File.WriteAllText(
            Path.Combine(outputDirectory, "kbo-report.md"),
            MarkdownRenderer.Render(report, vault.Root));
        File.WriteAllText(
            Path.Combine(outputDirectory, "kbo-report.gold.json"),
            JsonSerializer.Serialize(report, GoldJsonOptions));
        File.WriteAllText(
            Path.Combine(outputDirectory, "kbo-dashboard.gold.json"),
            JsonSerializer.Serialize(dashboard, GoldJsonOptions));
        File.WriteAllText(
            Path.Combine(outputDirectory, "kbo-dashboard.html"),
            DashboardRenderer.Render(dashboard, DashboardRenderer.LoadEmbeddedChartSpecs()));

        WriteDailyDigests(outputDirectory, digests);

        string fleetSummary = fleet is null
            ? string.Empty
            : $"; fleet: {fleet.Repos.Count} repo(s), {fleet.Behind} behind v{fleet.CurrentVersion}";
        output.WriteLine(
            $"report written to {outputDirectory}: {report.DeadNotes.Count} dead, {report.HotNotes.Count} hot, {report.StaleNotes.Count} stale, {report.LifecycleCounts.Values.Sum()} lifecycle and {report.MachineManagedCounts.Values.Sum()} machine-managed excluded, {report.DormantSources.Count} dormant source(s) (inventory {report.InventoryCounts.Values.Sum()}); dashboard: {dashboard.JobHealth.Count} job tile(s), {dashboard.JobHealth.Count(t => t.Status == "red")} red{fleetSummary}; {digests.Count} day page(s)");
        return 0;
    }

    private static void WriteDailyDigests(string outputDirectory, IReadOnlyList<DayDigest> digests)
    {
        string daysDirectory = Path.Combine(outputDirectory, "days");
        Directory.CreateDirectory(daysDirectory);
        File.WriteAllText(Path.Combine(daysDirectory, "index.md"), DailyDigestRenderer.RenderIndex(digests));
        foreach (DayDigest day in digests)
        {
            File.WriteAllText(Path.Combine(daysDirectory, day.Date + ".md"), DailyDigestRenderer.RenderDay(day));
        }
    }
}
