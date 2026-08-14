using System.Globalization;

namespace Kbo.Jobs;

/// <summary>
/// Puts a directory under local git and auto-commits every pulse. Registered
/// twice: vault-git (07 §Vault git, G2-12 — point-in-time note content for the
/// future judge, drift measurement, safety for bulk vault operations) and
/// bronze-git (ADR-0018 — tamper-evident history for the append-only event
/// store). No remote — durability is backup's job.
/// </summary>
public sealed class GitCommitJob : IPulseJob
{
    private readonly string root;
    private readonly IProcessRunner processRunner;
    private readonly TimeProvider clock;

    public GitCommitJob(string name, string root, IProcessRunner processRunner, TimeProvider clock)
    {
        Name = name;
        this.root = root;
        this.processRunner = processRunner;
        this.clock = clock;
    }

    public string Name { get; }
    public JobCadence Cadence => JobCadence.Daily;

    public string Run()
    {
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException($"repository root not found: {root}");
        }

        if (!Directory.Exists(Path.Combine(root, ".git")))
        {
            Git("init", "--quiet");
        }

        Git("add", "-A");

        ProcessResult status = Git("status", "--porcelain");
        if (status.StandardOutput.Trim().Length == 0)
        {
            return "no changes";
        }

        string message = "kbo auto-commit " + clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        Git("-c", "user.name=kbo", "-c", "user.email=kbo@localhost", "commit", "--quiet", "-m", message);

        int changedFiles = status.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        return $"committed {changedFiles} change(s)";
    }

    private ProcessResult Git(params string[] arguments)
    {
        List<string> fullArguments = new() { "-C", root };
        fullArguments.AddRange(arguments);
        ProcessResult result = processRunner.Run("git", fullArguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {result.StandardError.Trim()}");
        }
        return result;
    }
}
