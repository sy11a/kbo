namespace Kbo.Jobs;

public sealed class BackupJob : IPulseJob
{
    private readonly string repository;
    private readonly string passwordFile;
    private readonly IReadOnlyList<string> paths;
    private readonly IProcessRunner processRunner;

    public BackupJob(string repository, string passwordFile, IReadOnlyList<string> paths, IProcessRunner processRunner)
    {
        this.repository = repository;
        this.passwordFile = passwordFile;
        this.paths = paths;
        this.processRunner = processRunner;
    }

    public string Name => "backup";
    public JobCadence Cadence => JobCadence.Daily;

    public string Run()
    {
        List<string> backupArguments = new()
        {
            "--repo", repository, "--password-file", passwordFile, "backup", "--quiet",
        };
        backupArguments.AddRange(paths);
        Restic(backupArguments);

        Restic(new List<string>
        {
            "--repo", repository, "--password-file", passwordFile, "forget", "--quiet",
            "--keep-daily", "7", "--keep-weekly", "4", "--keep-monthly", "6", "--prune",
        });

        return $"paths={paths.Count} repo={repository}";
    }

    private void Restic(IReadOnlyList<string> arguments)
    {
        ProcessResult result = processRunner.Run("restic", arguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"restic exited with status {result.ExitCode}: {result.StandardError.Trim()}");
        }
    }
}
