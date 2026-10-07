using Kbo.Adapters.ClaudeCode;
using Kbo.Adapters.Opencode;
using Kbo.Bronze;
using Kbo.Jobs;
using Kbo.Registry;

namespace Kbo.Cli;

internal static class PulseCommand
{
    private const string Usage = "usage: kbo pulse";

    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string, string?> environment,
        string homeDirectory)
    {
        if (args.Length is not 0)
        {
            error.WriteLine(Usage);
            return 1;
        }

        KnowledgeRegistry? registry = TryLoadRegistry(environment, homeDirectory, error);
        if (registry is null)
        {
            return 1;
        }

        KnowledgeSource? vault = registry.Sources.FirstOrDefault(source => source.Layer is KnowledgeLayer.Global);
        string eventsRepo = environment(KboEnvironment.EventsRepoVariable)
            ?? KboEnvironment.DefaultEventsRepo(homeDirectory);
        string archiveRoot = environment(KboEnvironment.ArchiveRootVariable)
            ?? Path.Combine(homeDirectory, "Archive", "agent-transcripts");
        string resticRepo = environment(KboEnvironment.ResticRepoVariable)
            ?? Path.Combine(homeDirectory, "Backups", "kb-restic");
        string resticPasswordFile = Path.Combine(homeDirectory, ".config", "kb-observability", "restic-password");
        List<string> backupPaths = BuildBackupPaths(archiveRoot, eventsRepo, vault);

        List<IPulseJob> jobs = BuildJobs(
            registry,
            eventsRepo,
            archiveRoot,
            vault,
            resticRepo,
            resticPasswordFile,
            backupPaths,
            environment,
            homeDirectory);

        int failures = PulseRunner.Run(jobs, eventsRepo, registry.Machine, TimeProvider.System, CryptographicUlidEntropy.Instance, output);
        return failures is 0 ? 0 : 1;
    }

    private static KnowledgeRegistry? TryLoadRegistry(
        Func<string, string?> environment,
        string homeDirectory,
        TextWriter error)
    {
        try
        {
            return KnowledgeRegistry.Load(
                RegistryLocator.Locate(explicitPath: null, environment, homeDirectory),
                environment(KboEnvironment.TaskPatternVariable));
        }
        catch (RegistryFormatException exception)
        {
            error.WriteLine(exception.Message);
            return null;
        }
    }

    private static List<string> BuildBackupPaths(string archiveRoot, string eventsRepo, KnowledgeSource? vault)
    {
        List<string> backupPaths = [archiveRoot];
        if (vault is not null)
        {
            backupPaths.Add(vault.Root);
        }
        if (Directory.Exists(eventsRepo))
        {
            backupPaths.Add(eventsRepo);
        }
        return backupPaths;
    }

    private static List<IPulseJob> BuildJobs(
        KnowledgeRegistry registry,
        string eventsRepo,
        string archiveRoot,
        KnowledgeSource? vault,
        string resticRepo,
        string resticPasswordFile,
        List<string> backupPaths,
        Func<string, string?> environment,
        string homeDirectory)
    {
        ProcessRunner processRunner = new();
        List<IPulseJob> jobs =
        [
            new CommandJob("harvest", JobCadence.Daily,
                (jobOutput, jobError) => HarvestCommand.Run(
                    [ClaudeCodeAdapter.AgentName], jobOutput, jobError, environment, homeDirectory)),
            new CommandJob("harvest-opencode", JobCadence.Daily,
                (jobOutput, jobError) => HarvestCommand.Run(
                    [OpencodeRetention.AgentName], jobOutput, jobError, environment, homeDirectory)),
            new CommandJob("rebuild", JobCadence.Daily,
                (jobOutput, jobError) => RebuildCommand.Run(
                    [], jobOutput, jobError, environment, homeDirectory)),
            new IngestGraphMetricsJob(registry, eventsRepo, TimeProvider.System, CryptographicUlidEntropy.Instance),
            new ArchiveJob(
                archiveRoot,
                new[] { ClaudeCodeRetention.Manifest(homeDirectory), OpencodeRetention.Manifest(homeDirectory) },
                TimeProvider.System,
                processRunner),
        ];
        if (vault is not null)
        {
            jobs.Add(new GitCommitJob("vault-git", vault.Root, processRunner, TimeProvider.System));
        }
        if (Directory.Exists(eventsRepo))
        {
            jobs.Add(new GitCommitJob("bronze-git", eventsRepo, processRunner, TimeProvider.System));
        }
        jobs.Add(new BackupJob(resticRepo, resticPasswordFile, backupPaths, processRunner));
        jobs.Add(new CommandJob("report", JobDeadMan.CadenceOf("report"),
            (jobOutput, jobError) => ReportCommand.Run(
                [], jobOutput, jobError, environment, homeDirectory)));
        jobs.Add(new CommandJob("audit", JobDeadMan.CadenceOf("audit"),
            (jobOutput, jobError) => AuditCommand.Run(
                [], jobOutput, jobError, environment, homeDirectory)));
        return jobs;
    }
}
