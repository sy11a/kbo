using System.Globalization;
using System.Text;
using System.Text.Json;
using Kbo.Adapters.ClaudeCode;
using Kbo.Adapters.Opencode;
using Kbo.Gold;
using Kbo.Registry;

namespace Kbo.Cli;

internal static class AuditCommand
{
    private const string Usage = "usage: kbo audit [--out <dir>]";

    private static readonly JsonSerializerOptions _goldJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
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
            if (args[index] is "--out" && index + 1 < args.Length)
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
                RegistryLocator.Locate(explicitPath: null, environment, homeDirectory),
                environment(KboEnvironment.TaskPatternVariable));
        }
        catch (RegistryFormatException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }

        KnowledgeSource? vault = registry.Sources.FirstOrDefault(source => source.Layer is KnowledgeLayer.Global);
        if (vault is null)
        {
            error.WriteLine("registry has no global-layer source (the vault); cannot locate _generated/");
            return 1;
        }

        string eventsRepo = environment(KboEnvironment.EventsRepoVariable)
            ?? KboEnvironment.DefaultEventsRepo(homeDirectory);
        string silverPath = environment(KboEnvironment.SilverVariable)
            ?? KboEnvironment.DefaultSilverPath(homeDirectory);

        AuditReport report = AuditComputer.Compute(
            new[] { ClaudeCodeRetention.Manifest(homeDirectory), OpencodeRetention.Manifest(homeDirectory) },
            eventsRepo,
            silverPath,
            registry,
            TimeProvider.System);

        string outputDirectory = explicitOut ?? Path.Combine(vault.Root, "_generated");
        _ = Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "kbo-audit.md"), RenderMarkdown(report));
        File.WriteAllText(
            Path.Combine(outputDirectory, "kbo-audit.gold.json"),
            JsonSerializer.Serialize(report, _goldJsonOptions));

        output.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"audit written to {outputDirectory}: {report.MissingSessions.Sum(f => f.Count)} missing session file(s), {report.UnregisteredSources.Count} unregistered source dir(s)"));
        return 0;
    }

    private static string RenderMarkdown(AuditReport report)
    {
        StringBuilder markdown = new();
        _ = markdown.AppendLine("# kbo audit — capture completeness");
        _ = markdown.AppendLine();
        _ = markdown.AppendLine(CultureInfo.InvariantCulture,
            $"> **GENERATED** by `kbo audit` at **{report.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}** on `{report.Machine}` — hand-edits die on the next run.");
        _ = markdown.AppendLine();

        _ = markdown.AppendLine("## Missing sessions — on disk, never seen by bronze");
        _ = markdown.AppendLine();
        if (report.MissingSessions.Count is 0)
        {
            _ = markdown.AppendLine("none 🎉 — capture is complete for all session-auditable agents");
        }
        else
        {
            foreach (MissingSessionsFinding finding in report.MissingSessions)
            {
                _ = markdown.AppendLine(CultureInfo.InvariantCulture,
                    $"- **agent `{finding.Agent}` on `{finding.Machine}`: {finding.Count} session file(s) missing since {finding.MissingSince.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}** — recover with `kbo harvest {finding.Agent}`");
                foreach (string transcript in finding.Transcripts)
                {
                    _ = markdown.AppendLine(CultureInfo.InvariantCulture, $"  - `{transcript}`");
                }
            }
        }
        _ = markdown.AppendLine();

        if (report.AgentsWithoutSessionAudit.Count > 0)
        {
            _ = markdown.AppendLine(CultureInfo.InvariantCulture,
                $"_Not session-auditable yet: {string.Join(", ", report.AgentsWithoutSessionAudit)} (manifest declares no session files; coverage arrives with the agent's full adapter)._");
            _ = markdown.AppendLine();
        }

        _ = markdown.AppendLine("## Unregistered knowledge sources? — `.md` reads under no registered root");
        _ = markdown.AppendLine();
        if (report.UnregisteredSources.Count is 0)
        {
            _ = markdown.AppendLine("none");
        }
        else
        {
            _ = markdown.AppendLine("| Directory | Reads |");
            _ = markdown.AppendLine("|-----------|------:|");
            foreach (UnregisteredSourceFinding finding in report.UnregisteredSources)
            {
                _ = markdown.AppendLine(CultureInfo.InvariantCulture, $"| `{finding.Directory}` | {finding.ReadCount} |");
            }
            _ = markdown.AppendLine();
            _ = markdown.AppendLine("Add real knowledge roots to `~/.config/kbo/registry.yaml`; the registry is the denominator.");
        }

        return markdown.ToString();
    }
}
