using System.Diagnostics.CodeAnalysis;
using DuckDB.NET.Data;
using Kbo.Bronze;
using Kbo.Jobs;
using Kbo.Registry;
using Kbo.Silver;

namespace Kbo.Gold;

internal static class AuditComputer
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

        List<string> agentsWithoutSessionAudit = [];
        List<MissingSessionsFinding> missingSessions = [];
        foreach (RetentionManifest manifest in manifests)
        {
            if (manifest.SessionFiles is null && manifest.SessionDatabase is null)
            {
                agentsWithoutSessionAudit.Add(manifest.Agent);
                continue;
            }

            List<(string Stem, DateTime Modified)> missing = [];
            if (manifest.SessionFiles is not null && Directory.Exists(manifest.SessionFiles.Root))
            {
                foreach (string path in Directory
                    .EnumerateFiles(manifest.SessionFiles.Root, manifest.SessionFiles.Pattern, SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal))
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

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "SQL is the IdQuery from the operator's trusted knowledge registry; no user input is concatenated.")]
    private static List<(string Id, DateTime Modified)> EnumerateDatabaseSessions(SqliteSessionSource source)
    {
        List<(string, DateTime)> sessions = [];
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

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "SQL is a constant query authored in this file; only parameter values are bound, no string concatenation of external input.")]
    private static List<UnregisteredSourceFinding> QueryUnregisteredSources(string silverPath, KnowledgeRegistry registry)
    {
        List<UnregisteredSourceFinding> findings = [];
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
        using DuckDBDataReader reader = command.ExecuteReader();
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
