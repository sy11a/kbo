using System.Globalization;
using System.Text.Json;
using Kbo.Gold;
using Kbo.Registry;

namespace Kbo.Cli;

/// <summary>
/// Foreground live refresh (ADR-0022, Option A): rebuilds silver and re-renders
/// the dashboard on an interval, writing the same static HTML with a
/// self-reload meta tag so an open browser tab stays current. No server, no
/// resident daemon — the loop runs only while the command is in the foreground
/// and stops on cancellation (Ctrl-C).
/// </summary>
public static class WatchCommand
{
    public const int DefaultIntervalSeconds = 30;
    public const int MinIntervalSeconds = 5;

    private const string Usage = "usage: kbo watch [--interval <seconds>]";

    private static readonly JsonSerializerOptions GoldJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string, string?> environment,
        string homeDirectory,
        CancellationToken cancellationToken)
    {
        if (!TryParseInterval(args, out int intervalSeconds, out string? parseError))
        {
            error.WriteLine(parseError);
            return 1;
        }

        output.WriteLine(FormattableString.Invariant(
            $"kbo watch — refreshing the dashboard every {intervalSeconds}s; press Ctrl-C to stop"));

        int firstTick = RunOnce(output, error, environment, homeDirectory, intervalSeconds);
        if (firstTick != 0)
        {
            return firstTick;
        }

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(intervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                RunOnce(output, error, environment, homeDirectory, intervalSeconds);
            }
        }
        catch (OperationCanceledException)
        {
        }

        output.WriteLine("kbo watch stopped");
        return 0;
    }

    private static bool TryParseInterval(string[] args, out int intervalSeconds, out string? errorMessage)
    {
        intervalSeconds = DefaultIntervalSeconds;
        errorMessage = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--interval" && index + 1 < args.Length)
            {
                string raw = args[++index];
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                    || parsed < MinIntervalSeconds)
                {
                    errorMessage = $"--interval must be an integer >= {MinIntervalSeconds} (seconds)";
                    return false;
                }
                intervalSeconds = parsed;
            }
            else
            {
                errorMessage = Usage;
                return false;
            }
        }
        return true;
    }

    private static int RunOnce(
        TextWriter output,
        TextWriter error,
        Func<string, string?> environment,
        string homeDirectory,
        int intervalSeconds)
    {
        int rebuild = RebuildCommand.Run(Array.Empty<string>(), output, error, environment, homeDirectory);
        if (rebuild != 0)
        {
            return rebuild;
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
        DashboardGold dashboard = DashboardComputer.Compute(silverPath, registry, TimeProvider.System);

        string outputDirectory = Path.Combine(vault.Root, "_generated");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(
            Path.Combine(outputDirectory, "kbo-dashboard.gold.json"),
            JsonSerializer.Serialize(dashboard, GoldJsonOptions));
        File.WriteAllText(
            Path.Combine(outputDirectory, "kbo-dashboard.html"),
            DashboardRenderer.Render(dashboard, DashboardRenderer.LoadEmbeddedChartSpecs(), intervalSeconds));

        string stamp = TimeProvider.System.GetUtcNow().UtcDateTime.ToString("HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        int red = dashboard.JobHealth.Count(tile => tile.Status == "red");
        output.WriteLine(FormattableString.Invariant($"dashboard refreshed {stamp} — {red} red job tile(s)"));
        return 0;
    }
}
