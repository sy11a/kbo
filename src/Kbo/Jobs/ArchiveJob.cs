using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Kbo.Jobs;

public sealed class ArchiveJob : IPulseJob
{
    private readonly string archiveRoot;
    private readonly IReadOnlyList<RetentionManifest> manifests;
    private readonly TimeProvider clock;
    private readonly IProcessRunner processRunner;

    private int copied;
    private int skipped;

    public ArchiveJob(
        string archiveRoot,
        IReadOnlyList<RetentionManifest> manifests,
        TimeProvider clock,
        IProcessRunner processRunner)
    {
        this.archiveRoot = archiveRoot;
        this.manifests = manifests;
        this.clock = clock;
        this.processRunner = processRunner;
    }

    public string Name => "archive";
    public JobCadence Cadence => JobCadence.Daily;

    public string Run()
    {
        copied = 0;
        skipped = 0;
        Directory.CreateDirectory(archiveRoot);

        foreach (RetentionManifest manifest in manifests)
        {
            foreach (ArchiveEntry entry in manifest.Entries)
            {
                switch (entry)
                {
                    case FileTreeEntry tree:
                        ArchiveTree(tree);
                        break;
                    case SingleFileEntry file:
                        ArchiveFile(file.Path, Path.Combine(archiveRoot, file.Destination + ".zst"));
                        break;
                    case SqliteEntry sqlite:
                        ArchiveSqlite(sqlite);
                        break;
                }
            }
        }

        return $"copied={copied} skipped={skipped} root={archiveRoot}";
    }

    private void ArchiveTree(FileTreeEntry tree)
    {
        if (!Directory.Exists(tree.Root))
        {
            return;
        }
        foreach (string source in Directory.EnumerateFiles(tree.Root, tree.Pattern, SearchOption.AllDirectories).Order())
        {
            string relative = Path.GetRelativePath(tree.Root, source);
            ArchiveFile(source, Path.Combine(archiveRoot, tree.DestinationPrefix, relative + ".zst"));
        }
    }

    private void ArchiveFile(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }
        if (File.Exists(destination) && File.GetLastWriteTimeUtc(source) <= File.GetLastWriteTimeUtc(destination))
        {
            skipped++;
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        ProcessResult result = processRunner.Run("zstd", new[] { "-q", "-f", "-o", destination, "--", source });
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"zstd failed for {source}: {result.StandardError}");
        }
        copied++;
    }

    private void ArchiveSqlite(SqliteEntry sqlite)
    {
        if (!File.Exists(sqlite.DatabasePath))
        {
            return;
        }

        string latest = Path.Combine(archiveRoot, sqlite.DestinationPrefix, sqlite.LatestFileName + ".zst");
        if (!File.Exists(latest) || File.GetLastWriteTimeUtc(sqlite.DatabasePath) > File.GetLastWriteTimeUtc(latest))
        {
            string temporaryCopy = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");
            try
            {
                using (SqliteConnection source = new($"Data Source={sqlite.DatabasePath};Mode=ReadOnly"))
                using (SqliteConnection destination = new($"Data Source={temporaryCopy}"))
                {
                    source.Open();
                    destination.Open();
                    source.BackupDatabase(destination);
                }
                SqliteConnection.ClearAllPools();

                Directory.CreateDirectory(Path.GetDirectoryName(latest)!);
                ProcessResult result = processRunner.Run("zstd", new[] { "-q", "-f", "-o", latest, "--", temporaryCopy });
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException($"zstd failed for {sqlite.DatabasePath}: {result.StandardError}");
                }
                copied++;
            }
            finally
            {
                File.Delete(temporaryCopy);
            }
        }
        else
        {
            skipped++;
        }

        DateTimeOffset now = clock.GetUtcNow();
        int isoYear = System.Globalization.ISOWeek.GetYear(now.UtcDateTime);
        int isoWeek = System.Globalization.ISOWeek.GetWeekOfYear(now.UtcDateTime);
        string weeklyName = string.Create(
            CultureInfo.InvariantCulture, $"{sqlite.WeeklySnapshotPrefix}{isoYear}-W{isoWeek:D2}.db.zst");
        string weekly = Path.Combine(archiveRoot, sqlite.DestinationPrefix, weeklyName);
        if (!File.Exists(weekly))
        {
            File.Copy(latest, weekly);
            copied++;
        }
        else
        {
            skipped++;
        }
    }
}
