using System.Text.RegularExpressions;

namespace Kbo.Adapters.ClaudeCode;

public sealed record GitContext(string? RepoRoot, string? Branch, string? Task)
{
    private const string GitDirectoryName = ".git";
    private const string GitDirPointerPrefix = "gitdir:";
    private const string BranchRefPrefix = "ref: refs/heads/";

    private static readonly Regex TaskPattern = new(@"AC-\d+");

    public static string? TaskFromBranch(string? branch)
    {
        if (branch is null)
        {
            return null;
        }
        Match match = TaskPattern.Match(branch);
        return match.Success ? match.Value : null;
    }

    public static GitContext Discover(string? cwd)
    {
        if (string.IsNullOrEmpty(cwd))
        {
            return new GitContext(null, null, null);
        }

        string? directory = cwd;
        while (directory is not null)
        {
            string gitPath = Path.Combine(directory, GitDirectoryName);
            string? headPath = null;
            if (Directory.Exists(gitPath))
            {
                headPath = Path.Combine(gitPath, "HEAD");
            }
            else if (File.Exists(gitPath))
            {
                string pointer = File.ReadAllText(gitPath).Trim();
                if (pointer.StartsWith(GitDirPointerPrefix, StringComparison.Ordinal))
                {
                    string gitDirectory = pointer[GitDirPointerPrefix.Length..].Trim();
                    if (!Path.IsPathRooted(gitDirectory))
                    {
                        gitDirectory = Path.GetFullPath(Path.Combine(directory, gitDirectory));
                    }
                    headPath = Path.Combine(gitDirectory, "HEAD");
                }
            }

            if (headPath is not null)
            {
                string? branch = ReadBranch(headPath);
                string? task = branch is null ? null : TaskPattern.Match(branch) is { Success: true } match ? match.Value : null;
                return new GitContext(directory, branch, task);
            }

            directory = Path.GetDirectoryName(directory);
        }

        return new GitContext(null, null, null);
    }

    private static string? ReadBranch(string headPath)
    {
        if (!File.Exists(headPath))
        {
            return null;
        }

        string head = File.ReadAllText(headPath).Trim();
        if (head.StartsWith(BranchRefPrefix, StringComparison.Ordinal))
        {
            return head[BranchRefPrefix.Length..];
        }
        return head.Length > 0 ? head : null;
    }
}
