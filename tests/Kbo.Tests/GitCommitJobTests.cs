using System.Globalization;
using Kbo.Jobs;

namespace Kbo.Tests;

public class GitCommitJobTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-12T23:00:00Z", CultureInfo.InvariantCulture);

    private readonly string vaultRoot;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public GitCommitJobTests()
    {
        vaultRoot = Directory.CreateTempSubdirectory("kbo-gitcommit-tests").FullName;
        File.WriteAllText(Path.Combine(vaultRoot, "note.md"), "# v1\n");
    }

    public void Dispose()
    {
        Directory.Delete(vaultRoot, recursive: true);
    }

    private GitCommitJob Job(string name = "vault-git")
    {
        return new GitCommitJob(name, vaultRoot, new ProcessRunner(), new FixedTimeProvider(Now));
    }

    private string Git(params string[] arguments)
    {
        ProcessResult result = new ProcessRunner().Run("git", new[] { "-C", vaultRoot }.Concat(arguments).ToList());
        Assert.Equal(0, result.ExitCode);
        return result.StandardOutput.Trim();
    }

    [Fact]
    public void JobName_IsConfigurable_SoBronzeAndVaultShareTheImplementation()
    {
        Assert.Equal("vault-git", Job().Name);
        Assert.Equal("bronze-git", Job("bronze-git").Name);
    }

    [Fact]
    public void FirstRun_InitializesRepoAndCommitsEverything()
    {
        string summary = Job().Run();

        Assert.True(Directory.Exists(Path.Combine(vaultRoot, ".git")));
        Assert.Contains("committed", summary);
        Assert.Contains("kbo auto-commit 2026-08-12", Git("log", "-1", "--format=%s"));
        Assert.Equal("note.md", Git("show", "--name-only", "--format=", "HEAD"));
    }

    [Fact]
    public void NoChanges_CommitsNothing()
    {
        Job().Run();
        string headBefore = Git("rev-parse", "HEAD");

        string summary = Job().Run();

        Assert.Contains("no changes", summary);
        Assert.Equal(headBefore, Git("rev-parse", "HEAD"));
    }

    [Fact]
    public void ChangedNote_PointInTimeContentIsRetrievable()
    {
        Job().Run();
        string firstCommit = Git("rev-parse", "HEAD");
        File.WriteAllText(Path.Combine(vaultRoot, "note.md"), "# v2 — edited\n");

        Job().Run();

        Assert.Equal("# v1", Git("show", $"{firstCommit}:note.md"));
        Assert.Equal("# v2 — edited", Git("show", "HEAD:note.md"));
        Assert.NotEqual(firstCommit, Git("rev-parse", "HEAD"));
    }
}
