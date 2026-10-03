using System.Globalization;

namespace Kbo.Jobs;

internal sealed class BackupJob : IPulseJob
{
    private readonly string _repository;
    private readonly string _passwordFile;
    private readonly IReadOnlyList<string> _paths;
    private readonly IProcessRunner _processRunner;

    public BackupJob(string repository, string passwordFile, IReadOnlyList<string> paths, IProcessRunner processRunner)
    {
        _repository = repository;
        _passwordFile = passwordFile;
        _paths = paths;
        _processRunner = processRunner;
    }

    public string Name => "backup";
    public JobCadence Cadence => JobCadence.Daily;

    public string Run()
    {
        List<string> backupArguments =
        [
            "--repo", _repository, "--password-file", _passwordFile, "backup", "--quiet", .. _paths,
        ];
        Restic(backupArguments);

        Restic(new List<string>
        {
            "--repo", _repository, "--password-file", _passwordFile, "forget", "--quiet",
            "--keep-daily", "7", "--keep-weekly", "4", "--keep-monthly", "6", "--prune",
        });

        return $"paths={_paths.Count} repo={_repository}";
    }

    private void Restic(IReadOnlyList<string> arguments)
    {
        ProcessResult result = _processRunner.Run("restic", arguments);
        if (result.ExitCode is 0)
        {
            return;
        }
        throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"restic exited with status {result.ExitCode}: {result.StandardError.Trim()}"));
    }
}
