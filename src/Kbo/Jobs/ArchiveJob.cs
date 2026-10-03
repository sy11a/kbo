using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Kbo.Jobs;

internal sealed class ArchiveJob : IPulseJob
{
    private readonly string _archiveRoot;
    private readonly IReadOnlyList<RetentionManifest> _manifests;
    private readonly TimeProvider _clock;
    private readonly IProcessRunner _processRunner;

    private int _copied;
    private int _skipped;

    public ArchiveJob(
        string archiveRoot,
        IReadOnlyList<RetentionManifest> manifests,
        TimeProvider clock,
        IProcessRunner processRunner)
    {
        _archiveRoot = archiveRoot;
        _manifests = manifests;
        _clock = clock;
        _processRunner = processRunner;
    }

    public string Name => "archive";
    public JobCadence Cadence => JobCadence.Daily;

    public string Run()
    {
        _copied = 0;
        _skipped = 0;
        _ = Directory.CreateDirectory(_archiveRoot);

        foreach (RetentionManifest manifest in _manifests)
        {
            foreach (ArchiveEntry entry in manifest.Entries)
            {
                switch (entry)
                {
                    case FileTreeEntry tree:
                        {
                            ArchiveTree(tree);
                            break;
                        }
                    case SingleFileEntry file:
                        {
                            ArchiveFile(file.Path, Path.Combine(_archiveRoot, file.Destination + ".zst"));
                            break;
                        }
                    case SqliteEntry sqlite:
                        {
                            ArchiveSqlite(sqlite);
                            break;
                        }
                }
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"copied={_copied} skipped={_skipped} root={_archiveRoot}");
    }

    private void ArchiveTree(FileTreeEntry tree)
    {
        if (!Directory.Exists(tree.Root))
        {
            return;
        }
        foreach (string source in Directory.EnumerateFiles(tree.Root, tree.Pattern, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(tree.Root, source);
            ArchiveFile(source, Path.Combine(_archiveRoot, tree.DestinationPrefix, relative + ".zst"));
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
            _skipped++;
            return;
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        ProcessResult result = _processRunner.Run("zstd", new[] { "-q", "-f", "-o", destination, "--", source });
        if (result.ExitCode is not 0)
        {
            throw new InvalidOperationException($"zstd failed for {source}: {result.StandardError}");
        }
        _copied++;
    }

    private void ArchiveSqlite(SqliteEntry sqlite)
    {
        if (!File.Exists(sqlite.DatabasePath))
        {
            return;
        }

        string latest = Path.Combine(_archiveRoot, sqlite.DestinationPrefix, sqlite.LatestFileName + ".zst");
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

                _ = Directory.CreateDirectory(Path.GetDirectoryName(latest)!);
                ProcessResult result = _processRunner.Run("zstd", new[] { "-q", "-f", "-o", latest, "--", temporaryCopy });
                if (result.ExitCode is not 0)
                {
                    throw new InvalidOperationException($"zstd failed for {sqlite.DatabasePath}: {result.StandardError}");
                }
                _copied++;
            }
            finally
            {
                File.Delete(temporaryCopy);
            }
        }
        else
        {
            _skipped++;
        }

        DateTimeOffset now = _clock.GetUtcNow();
        int isoYear = ISOWeek.GetYear(now.UtcDateTime);
        int isoWeek = ISOWeek.GetWeekOfYear(now.UtcDateTime);
        string weeklyName = string.Create(
            CultureInfo.InvariantCulture, $"{sqlite.WeeklySnapshotPrefix}{isoYear}-W{isoWeek:D2}.db.zst");
        string weekly = Path.Combine(_archiveRoot, sqlite.DestinationPrefix, weeklyName);
        if (!File.Exists(weekly))
        {
            File.Copy(latest, weekly);
            _copied++;
        }
        else
        {
            _skipped++;
        }
    }
}
