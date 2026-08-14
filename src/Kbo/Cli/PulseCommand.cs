using Kbo.Adapters.ClaudeCode;
using Kbo.Adapters.Opencode;
using Kbo.Jobs;
using Kbo.Registry;

namespace Kbo.Cli;

public static class PulseCommand
{
    private const string Usage = "usage: kbo pulse";

    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string, string?> environment,
        string homeDirectory)
    {
        if (args.Length != 0)
        {
            error.WriteLine(Usage);
            return 1;
        }

        KnowledgeRegistry registry;
        try
        {
            registry = KnowledgeRegistry.Load(RegistryLocator.Locate(null, environment, homeDirectory));
        }
        catch (RegistryFormatException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }

        string eventsRepo = environment(KboEnvironment.EventsRepoVariable)
            ?? KboEnvironment.DefaultEventsRepo(homeDirectory);
        string archiveRoot = environment(KboEnvironment.ArchiveRootVariable)
            ?? Path.Combine(homeDirectory, "Archive", "agent-transcripts");
        string resticRepo = environment(KboEnvironment.ResticRepoVariable)
            ?? Path.Combine(homeDirectory, "Backups", "kb-restic");
        string resticPasswordFile = Path.Combine(homeDirectory, ".config", "kb-observability", "restic-password");

        List<string> backupPaths = new() { archiveRoot, };
        KnowledgeSource? vault = registry.Sources.FirstOrDefault(source => source.Layer == KnowledgeLayer.Global);
        if (vault is not null)
        {
            backupPaths.Add(vault.Root);
        }
        if (Directory.Exists(eventsRepo))
        {
            backupPaths.Add(eventsRepo);
        }

        ProcessRunner processRunner = new();
        List<IPulseJob> jobs = new()
        {
            new CommandJob("harvest", JobCadence.Daily,
                (jobOutput, jobError) => HarvestCommand.Run(
                    new[] { ClaudeCodeAdapter.AgentName }, jobOutput, jobError, environment, homeDirectory)),
            new CommandJob("harvest-opencode", JobCadence.Daily,
                (jobOutput, jobError) => HarvestCommand.Run(
                    new[] { OpencodeRetention.AgentName }, jobOutput, jobError, environment, homeDirectory)),
            new CommandJob("rebuild", JobCadence.Daily,
                (jobOutput, jobError) => RebuildCommand.Run(
                    Array.Empty<string>(), jobOutput, jobError, environment, homeDirectory)),
            new ArchiveJob(
                archiveRoot,
                new[] { ClaudeCodeRetention.Manifest(homeDirectory), OpencodeRetention.Manifest(homeDirectory) },
                TimeProvider.System,
                processRunner),
        };
        if (vault is not null)
        {
            jobs.Add(new GitCommitJob("vault-git", vault.Root, processRunner, TimeProvider.System));
        }
        if (Directory.Exists(eventsRepo))
        {
            jobs.Add(new GitCommitJob("bronze-git", eventsRepo, processRunner, TimeProvider.System));
        }
        jobs.Add(new BackupJob(resticRepo, resticPasswordFile, backupPaths, processRunner));
        jobs.Add(new CommandJob("report", JobCadence.Weekly,
            (jobOutput, jobError) => ReportCommand.Run(
                Array.Empty<string>(), jobOutput, jobError, environment, homeDirectory)));
        jobs.Add(new CommandJob("audit", JobCadence.Weekly,
            (jobOutput, jobError) => AuditCommand.Run(
                Array.Empty<string>(), jobOutput, jobError, environment, homeDirectory)));

        int failures = PulseRunner.Run(jobs, eventsRepo, registry.Machine, TimeProvider.System, Random.Shared, output);
        return failures == 0 ? 0 : 1;
    }
}
