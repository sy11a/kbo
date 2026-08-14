using Kbo.Jobs;

namespace Kbo.Tests;

public class BackupJobTests
{
    private sealed class FakeRunner(int exitCode = 0, string stderr = "") : IProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Invocations { get; } = new();

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Invocations.Add((fileName, arguments));
            return new ProcessResult(exitCode, "", stderr);
        }
    }

    [Fact]
    public void Run_InvokesResticBackupThenForget()
    {
        FakeRunner runner = new();
        BackupJob job = new("/backups/repo", "/secrets/pw", new[] { "/archive", "/vault" }, runner);

        string summary = job.Run();

        Assert.Equal(2, runner.Invocations.Count);
        Assert.All(runner.Invocations, invocation => Assert.Equal("restic", invocation.FileName));

        IReadOnlyList<string> backupArguments = runner.Invocations[0].Arguments;
        Assert.Contains("backup", backupArguments);
        Assert.Contains("/archive", backupArguments);
        Assert.Contains("/vault", backupArguments);
        Assert.Contains("/backups/repo", backupArguments);

        IReadOnlyList<string> forgetArguments = runner.Invocations[1].Arguments;
        Assert.Contains("forget", forgetArguments);
        Assert.Contains("--prune", forgetArguments);
        Assert.Contains("paths=2", summary);
    }

    [Fact]
    public void Run_ResticFailure_ThrowsWithStderr()
    {
        FakeRunner runner = new(exitCode: 1, stderr: "repository locked");
        BackupJob job = new("/backups/repo", "/secrets/pw", new[] { "/archive" }, runner);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => job.Run());
        Assert.Contains("repository locked", exception.Message);
    }
}
