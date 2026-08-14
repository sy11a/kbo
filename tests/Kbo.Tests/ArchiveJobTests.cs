using System.Globalization;
using Kbo.Jobs;
using Microsoft.Data.Sqlite;

namespace Kbo.Tests;

public class ArchiveJobTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-12T18:00:00Z", CultureInfo.InvariantCulture);

    private readonly string workspace;
    private readonly string sourceRoot;
    private readonly string archiveRoot;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public ArchiveJobTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-archive-tests").FullName;
        sourceRoot = Path.Combine(workspace, "projects");
        archiveRoot = Path.Combine(workspace, "archive");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "proj-a"));
        File.WriteAllText(Path.Combine(sourceRoot, "proj-a", "sess-1.jsonl"), "{\"a\":1}\n");
        File.WriteAllText(Path.Combine(sourceRoot, "proj-a", "notes.txt"), "not a transcript");
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private ArchiveJob Job(params ArchiveEntry[] entries)
    {
        return new ArchiveJob(
            archiveRoot,
            new[] { new RetentionManifest("test-agent", entries) },
            new FixedTimeProvider(Now),
            new ProcessRunner());
    }

    [Fact]
    public void FileTree_CompressesMatchingFiles_SecondRunSkips()
    {
        ArchiveJob job = Job(new FileTreeEntry(sourceRoot, "*.jsonl", "test-agent/projects"));

        string firstSummary = job.Run();
        string secondSummary = job.Run();

        string archived = Path.Combine(archiveRoot, "test-agent", "projects", "proj-a", "sess-1.jsonl.zst");
        Assert.True(File.Exists(archived));
        Assert.False(File.Exists(Path.Combine(archiveRoot, "test-agent", "projects", "proj-a", "notes.txt.zst")));
        Assert.Contains("copied=1", firstSummary);
        Assert.Contains("copied=0", secondSummary);
    }

    [Fact]
    public void FileTree_ChangedSource_IsRecompressed()
    {
        ArchiveJob job = Job(new FileTreeEntry(sourceRoot, "*.jsonl", "test-agent/projects"));
        job.Run();
        string sourceFile = Path.Combine(sourceRoot, "proj-a", "sess-1.jsonl");
        File.WriteAllText(sourceFile, "{\"a\":2}\n");
        File.SetLastWriteTimeUtc(sourceFile, DateTime.UtcNow.AddMinutes(5));

        string summary = job.Run();

        Assert.Contains("copied=1", summary);
    }

    [Fact]
    public void Sqlite_ConsistentCopyAndWeeklySnapshot()
    {
        string databasePath = Path.Combine(workspace, "opencode.db");
        using (SqliteConnection connection = new($"Data Source={databasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE sessions (id TEXT); INSERT INTO sessions VALUES ('s1');";
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        ArchiveJob job = Job(new SqliteEntry(databasePath, "opencode", "opencode-latest.db", "opencode-"));
        string summary = job.Run();

        Assert.True(File.Exists(Path.Combine(archiveRoot, "opencode", "opencode-latest.db.zst")));
        Assert.True(File.Exists(Path.Combine(archiveRoot, "opencode", "opencode-2026-W33.db.zst")));
        Assert.Contains("copied=2", summary);

        Assert.Contains("copied=0", job.Run());
    }

    [Fact]
    public void MissingSourceRoots_AreSkippedQuietly()
    {
        ArchiveJob job = Job(
            new FileTreeEntry(Path.Combine(workspace, "nonexistent"), "*", "x"),
            new SqliteEntry(Path.Combine(workspace, "no.db"), "y", "latest.db", "y-"),
            new SingleFileEntry(Path.Combine(workspace, "no-file.jsonl"), "z/history.jsonl"));

        Assert.Contains("copied=0", job.Run());
    }
}
