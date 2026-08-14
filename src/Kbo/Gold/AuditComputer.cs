using DuckDB.NET.Data;
using Kbo.Bronze;
using Kbo.Jobs;
using Kbo.Registry;
using Kbo.Silver;

namespace Kbo.Gold;

public static class AuditComputer
{
    public const int TranscriptListCap = 50;
    public const int UnregisteredSourceCap = 20;

    public static AuditReport Compute(
        IReadOnlyList<RetentionManifest> manifests,
        string eventsRepo,
        string silverPath,
        KnowledgeRegistry registry,
        TimeProvider clock)
    {
        string machine = registry.Machine;
        IReadOnlySet<string> seenTranscripts = new BronzeStore(eventsRepo).SeenTranscripts();

        List<string> agentsWithoutSessionAudit = new();
        List<MissingSessionsFinding> missingSessions = new();
        foreach (RetentionManifest manifest in manifests)
        {
            if (manifest.SessionFiles is null && manifest.SessionDatabase is null)
            {
                agentsWithoutSessionAudit.Add(manifest.Agent);
                continue;
            }

            List<(string Stem, DateTime Modified)> missing = new();
            if (manifest.SessionFiles is not null && Directory.Exists(manifest.SessionFiles.Root))
            {
                foreach (string path in Directory
                    .EnumerateFiles(manifest.SessionFiles.Root, manifest.SessionFiles.Pattern, SearchOption.AllDirectories)
                    .Order())
                {
                    string stem = Path.GetFileNameWithoutExtension(path);
                    if (!seenTranscripts.Contains(stem))
                    {
                        missing.Add((stem, File.GetLastWriteTimeUtc(path)));
                    }
                }
            }
            if (manifest.SessionDatabase is not null)
            {
                foreach ((string id, DateTime modified) in EnumerateDatabaseSessions(manifest.SessionDatabase))
                {
                    if (!seenTranscripts.Contains(id))
                    {
                        missing.Add((id, modified));
                    }
                }
            }

            if (missing.Count > 0)
            {
                missingSessions.Add(new MissingSessionsFinding(
                    manifest.Agent,
                    machine,
                    missing.Count,
                    new DateTimeOffset(missing.Min(entry => entry.Modified)),
                    missing.Select(entry => entry.Stem).Take(TranscriptListCap).ToList()));
            }
        }

        return new AuditReport(
            clock.GetUtcNow(),
            machine,
            agentsWithoutSessionAudit,
            missingSessions,
            QueryUnregisteredSources(silverPath, registry));
    }

    private static List<(string Id, DateTime Modified)> EnumerateDatabaseSessions(SqliteSessionSource source)
    {
        List<(string, DateTime)> sessions = new();
        if (!File.Exists(source.DatabasePath))
        {
            return sessions;
        }

        using Microsoft.Data.Sqlite.SqliteConnection connection = new($"Data Source={source.DatabasePath};Mode=ReadOnly;Pooling=false");
        connection.Open();
        using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = source.IdQuery;
        using Microsoft.Data.Sqlite.SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add((reader.GetString(0), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)).UtcDateTime));
        }
        return sessions;
    }

    private static List<UnregisteredSourceFinding> QueryUnregisteredSources(string silverPath, KnowledgeRegistry registry)
    {
        List<UnregisteredSourceFinding> findings = new();
        if (!File.Exists(silverPath))
        {
            return findings;
        }

        using DuckDBConnection connection = SilverConnection.OpenReadOnly(silverPath);
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT regexp_replace(subject, '/[^/]+$', '') AS directory, count(*) AS reads
            FROM events_preferred
            WHERE type = 'knowledge.read'
              AND kbroot IS NULL
              AND subject LIKE '%.md'
            GROUP BY directory
            ORDER BY reads DESC, directory
            LIMIT {UnregisteredSourceCap}
            """;
        using DuckDBDataReader reader = (DuckDBDataReader)command.ExecuteReader();
        while (reader.Read())
        {
            string directory = reader.GetString(0);
            if (registry.Resolve(directory) is null)
            {
                findings.Add(new UnregisteredSourceFinding(directory, reader.GetInt64(1)));
            }
        }
        return findings;
    }
}
