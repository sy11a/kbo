using System.Globalization;
using System.Numerics;
using DuckDB.NET.Data;
using Kbo.Jobs;
using Kbo.Registry;
using Kbo.Schemas;
using Kbo.Silver;

namespace Kbo.Gold;

public static class DashboardComputer
{
    public const int DeadManThresholdDays = 3;
    public const int ThemeWindowDays = 60;
    public const int ThemeChartLimit = 20;
    public const int RepoListCap = 50;
    public const int RecentSessionCap = 30;
    public const int TopListCap = 15;

    public static DashboardGold Compute(string silverPath, KnowledgeRegistry registry, TimeProvider clock,
        ConstitutionFleetGold? constitutionFleet = null)
    {
        DateTimeOffset now = clock.GetUtcNow();
        using DuckDBConnection connection = SilverConnection.OpenReadOnly(silverPath);

        HashSet<string> touchedSessions = TouchedSessions(connection, registry);
        (List<ThemeReadsRow> themeReads, List<ThemeReadsRow> unusedThemes) = ReadsByTheme(connection, registry, now);
        (List<ReuseRow> topReused, ReuseSummary reuseSummary) = NoteReuse(connection, registry, now);
        (List<WriteReadRow> topWriteRead, WriteReadSummary writeReadSummary) = WriteReadLoop(connection, registry, now);
        return new DashboardGold(
            now,
            registry.Machine,
            DeadManThresholdDays,
            JobDeadMan.WeeklyThresholdDays,
            JobHealth(connection, now),
            LastSeen(connection, now),
            constitutionFleet,
            ServiceSessions(connection, now),
            ReadsByLayer(connection, registry),
            FailedSearches(connection),
            KbTouch(connection, touchedSessions),
            Tokens(connection),
            themeReads,
            unusedThemes,
            SessionsByRepo(connection, now),
            RecentSessions(connection, touchedSessions),
            TopSkills(connection, now),
            TopFailedSearches(connection, now),
            ReadsByContentType(connection, registry, now),
            topReused,
            reuseSummary,
            topWriteRead,
            writeReadSummary,
            WeekOverWeek(connection, registry, touchedSessions, now));
    }

    private static List<MetricDelta> WeekOverWeek(DuckDBConnection connection, KnowledgeRegistry registry, HashSet<string> touchedSessions, DateTimeOffset now)
    {
        DateTime thisStart = now.AddDays(-7).UtcDateTime;
        DateTime lastStart = now.AddDays(-14).UtcDateTime;
        int Window(DateTime time) => time >= thisStart ? 0 : 1;

        long[] sessions = new long[2];
        long[] touched = new long[2];
        foreach (object?[] row in Query(connection, """
            SELECT started_at, session FROM sessions
            WHERE started_at >= $cutoff
              AND session NOT IN (SELECT session FROM service_sessions)
            """, ("cutoff", lastStart)))
        {
            int window = Window((DateTime)row[0]!);
            sessions[window]++;
            if (touchedSessions.Contains((string)row[1]!))
            {
                touched[window]++;
            }
        }

        long[] searches = new long[2];
        long[] zeroHits = new long[2];
        foreach (object?[] row in Query(connection, """
            SELECT time, TRY_CAST(json_extract_string(data, '$.hits') AS BIGINT) AS hits
            FROM practice_events
            WHERE type = 'knowledge.searched' AND time >= $cutoff
              AND TRY_CAST(json_extract_string(data, '$.hits') AS BIGINT) IS NOT NULL
            """, ("cutoff", lastStart)))
        {
            int window = Window((DateTime)row[0]!);
            searches[window]++;
            if (AsLong(row[1]) == 0)
            {
                zeroHits[window]++;
            }
        }

        long[] noteReads = new long[2];
        foreach (object?[] row in Query(connection, """
            SELECT time, subject FROM practice_events
            WHERE type = 'knowledge.read' AND subject IS NOT NULL AND time >= $cutoff
            """, ("cutoff", lastStart)))
        {
            string subject = (string)row[1]!;
            if (registry.Resolve(subject) is null || ContentKind.Of(subject) != ContentKind.Knowledge)
            {
                continue;
            }
            noteReads[Window((DateTime)row[0]!)]++;
        }

        double Rate(long numerator, long denominator) => denominator == 0 ? 0 : (double)numerator / denominator;
        return new List<MetricDelta>
        {
            new("KB-touch rate", Rate(touched[0], sessions[0]), Rate(touched[1], sessions[1]), "percent", true),
            new("Failed-search rate", Rate(zeroHits[0], searches[0]), Rate(zeroHits[1], searches[1]), "percent", false),
            new("Knowledge reads", noteReads[0], noteReads[1], "count", true),
        };
    }

    private static (List<WriteReadRow> Top, WriteReadSummary Summary) WriteReadLoop(DuckDBConnection connection, KnowledgeRegistry registry, DateTimeOffset now)
    {
        DateTime cutoff = now.AddDays(-ThemeWindowDays).UtcDateTime;

        Dictionary<string, DateTime> firstWrite = new();
        foreach (object?[] row in Query(connection, """
            SELECT subject, min(time) AS first_write
            FROM practice_events
            WHERE type = 'knowledge.written' AND subject IS NOT NULL AND time >= $cutoff
            GROUP BY subject
            """, ("cutoff", cutoff)))
        {
            string subject = (string)row[0]!;
            if (registry.Resolve(subject) is null || ContentKind.Of(subject) != ContentKind.Knowledge)
            {
                continue;
            }
            firstWrite[subject] = (DateTime)row[1]!;
        }

        Dictionary<string, long> laterReads = new();
        foreach (object?[] row in Query(connection, """
            SELECT subject, time
            FROM practice_events
            WHERE type = 'knowledge.read' AND subject IS NOT NULL AND time >= $cutoff
            """, ("cutoff", cutoff)))
        {
            string subject = (string)row[0]!;
            if (firstWrite.TryGetValue(subject, out DateTime written) && (DateTime)row[1]! > written)
            {
                laterReads[subject] = laterReads.GetValueOrDefault(subject) + 1;
            }
        }

        List<WriteReadRow> top = laterReads
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(TopListCap)
            .Select(entry => new WriteReadRow(entry.Key, entry.Value))
            .ToList();
        long writtenCount = firstWrite.Count;
        return (top, new WriteReadSummary(writtenCount, laterReads.Count, writtenCount == 0 ? 0 : (double)laterReads.Count / writtenCount));
    }

    private static (List<ReuseRow> Top, ReuseSummary Summary) NoteReuse(DuckDBConnection connection, KnowledgeRegistry registry, DateTimeOffset now)
    {
        List<ReuseRow> notes = new();
        foreach (object?[] row in Query(connection, """
            SELECT subject, count(*) AS reads, count(DISTINCT session) AS sessions
            FROM practice_events
            WHERE type = 'knowledge.read' AND subject IS NOT NULL AND time >= $cutoff
            GROUP BY subject
            """, ("cutoff", now.AddDays(-ThemeWindowDays).UtcDateTime)))
        {
            string subject = (string)row[0]!;
            if (registry.Resolve(subject) is null || ContentKind.Of(subject) != ContentKind.Knowledge)
            {
                continue;
            }
            notes.Add(new ReuseRow(subject, AsLong(row[2]), AsLong(row[1])));
        }

        long singleUse = notes.Count(note => note.Sessions <= 1);
        List<ReuseRow> top = notes
            .OrderByDescending(note => note.Sessions)
            .ThenByDescending(note => note.Reads)
            .ThenBy(note => note.Path, StringComparer.Ordinal)
            .Take(TopListCap)
            .ToList();
        return (top, new ReuseSummary(notes.Count, singleUse, notes.Count == 0 ? 0 : (double)singleUse / notes.Count));
    }

    private static List<DayCount> ReadsByContentType(DuckDBConnection connection, KnowledgeRegistry registry, DateTimeOffset now)
    {
        Dictionary<string, long> byKind = new();
        foreach (object?[] row in Query(connection, """
            SELECT subject, count(*)
            FROM practice_events
            WHERE type IN ('knowledge.read', 'context.loaded') AND subject IS NOT NULL AND time >= $cutoff
            GROUP BY subject
            """, ("cutoff", now.AddDays(-ThemeWindowDays).UtcDateTime)))
        {
            string subject = (string)row[0]!;
            if (registry.Resolve(subject) is null)
            {
                continue;
            }
            string kind = ContentKind.Of(subject);
            byKind[kind] = byKind.GetValueOrDefault(kind) + AsLong(row[1]);
        }
        return byKind
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new DayCount(entry.Key, entry.Value))
            .ToList();
    }

    private static List<DayCount> TopSkills(DuckDBConnection connection, DateTimeOffset now)
    {
        List<DayCount> rows = new();
        foreach (object?[] row in Query(connection, $"""
            SELECT json_extract_string(data, '$.skill') AS skill, count(*)
            FROM events_preferred
            WHERE type = 'skill.invoked' AND time >= $cutoff
              AND json_extract_string(data, '$.skill') IS NOT NULL
            GROUP BY skill
            ORDER BY count(*) DESC, skill
            LIMIT {TopListCap}
            """, ("cutoff", now.AddDays(-ThemeWindowDays).UtcDateTime)))
        {
            rows.Add(new DayCount((string)row[0]!, AsLong(row[1])));
        }
        return rows;
    }

    private static List<DayCount> TopFailedSearches(DuckDBConnection connection, DateTimeOffset now)
    {
        List<DayCount> rows = new();
        foreach (object?[] row in Query(connection, $"""
            SELECT subject, count(*)
            FROM practice_events
            WHERE type = 'knowledge.searched' AND time >= $cutoff
              AND subject IS NOT NULL
              AND TRY_CAST(json_extract_string(data, '$.hits') AS BIGINT) = 0
            GROUP BY subject
            ORDER BY count(*) DESC, subject
            LIMIT {TopListCap}
            """, ("cutoff", now.AddDays(-ThemeWindowDays).UtcDateTime)))
        {
            rows.Add(new DayCount((string)row[0]!, AsLong(row[1])));
        }
        return rows;
    }

    private static HashSet<string> TouchedSessions(DuckDBConnection connection, KnowledgeRegistry registry)
    {
        HashSet<string> registeredIds = registry.Sources.Select(source => source.Id).ToHashSet();
        HashSet<string> touchedSessions = new();
        foreach (object?[] row in Query(connection, """
            SELECT DISTINCT session, subject, kbroot
            FROM events_preferred
            WHERE session IS NOT NULL AND (subject IS NOT NULL OR kbroot IS NOT NULL)
            """))
        {
            string session = (string)row[0]!;
            if (touchedSessions.Contains(session))
            {
                continue;
            }
            bool resolvesNow = row[1] is string subject && registry.Resolve(subject) is not null;
            bool stampStillRegistered = row[2] is string kbroot && registeredIds.Contains(kbroot);
            if (resolvesNow || stampStillRegistered)
            {
                touchedSessions.Add(session);
            }
        }
        return touchedSessions;
    }

    private static List<RecentSessionRow> RecentSessions(DuckDBConnection connection, HashSet<string> touchedSessions)
    {
        Dictionary<string, long[]> countsBySession = new();
        foreach (object?[] row in Query(connection, """
            SELECT session, type, count(*)
            FROM events_preferred
            WHERE session IS NOT NULL
              AND type IN ('knowledge.read', 'knowledge.searched', 'skill.invoked', 'knowledge.written')
            GROUP BY session, type
            """))
        {
            string session = (string)row[0]!;
            long[] slot = countsBySession.TryGetValue(session, out long[]? existing) ? existing : countsBySession[session] = new long[4];
            int index = (string)row[1]! switch
            {
                EventTypes.KnowledgeRead => 0,
                EventTypes.KnowledgeSearched => 1,
                EventTypes.SkillInvoked => 2,
                EventTypes.KnowledgeWritten => 3,
                _ => -1,
            };
            if (index >= 0)
            {
                slot[index] = AsLong(row[2]);
            }
        }

        List<RecentSessionRow> rows = new();
        foreach (object?[] row in Query(connection, $"""
            SELECT session, agent, coalesce(repo, '(unknown)') AS repo, started_at,
                   coalesce(input_tokens, 0), coalesce(cache_read_tokens, 0)
            FROM sessions
            ORDER BY started_at DESC
            LIMIT {RecentSessionCap}
            """))
        {
            string session = (string)row[0]!;
            DateTime started = (DateTime)row[3]!;
            long[] counts = countsBySession.GetValueOrDefault(session) ?? new long[4];
            rows.Add(new RecentSessionRow(
                started.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                started.ToString("HH:mm", CultureInfo.InvariantCulture),
                (string)row[1]!, (string)row[2]!,
                counts[0], counts[1], counts[2], counts[3],
                touchedSessions.Contains(session), AsLong(row[4]), AsLong(row[5])));
        }
        return rows;
    }

    private static List<RepoSessionsRow> SessionsByRepo(DuckDBConnection connection, DateTimeOffset now)
    {
        List<RepoSessionsRow> rows = new();
        foreach (object?[] row in Query(connection, $"""
            SELECT coalesce(repo, '(unknown)') AS repo,
                   count(*) AS sessions,
                   string_agg(DISTINCT agent, ', ' ORDER BY agent) AS agents,
                   max(started_at) AS last_started
            FROM sessions
            WHERE started_at >= $cutoff
            GROUP BY repo
            ORDER BY sessions DESC, repo
            LIMIT {RepoListCap}
            """, ("cutoff", now.AddDays(-ThemeWindowDays).UtcDateTime)))
        {
            rows.Add(new RepoSessionsRow((string)row[0]!, AsLong(row[1]), (string)row[2]!, AsUtc(row[3])));
        }
        return rows;
    }

    private static List<JobHealthTile> JobHealth(DuckDBConnection connection, DateTimeOffset now)
    {
        List<JobHealthTile> tiles = new();
        foreach (object?[] row in Query(connection, """
            SELECT machine, agent, subject, max(time)
            FROM events
            WHERE type = 'job.completed' AND subject IS NOT NULL
            GROUP BY machine, agent, subject
            ORDER BY machine, agent, subject
            """))
        {
            DateTimeOffset last = AsUtc(row[3]);
            string job = (string)row[2]!;
            double daysSilent = (now - last).TotalDays;
            tiles.Add(new JobHealthTile(
                (string)row[0]!, (string)row[1]!, job, last,
                Math.Round(daysSilent, 1),
                daysSilent > JobDeadMan.ThresholdDays(job) ? "red" : "ok"));
        }
        return tiles;
    }

    private static List<LastSeenTile> LastSeen(DuckDBConnection connection, DateTimeOffset now)
    {
        List<LastSeenTile> tiles = new();
        foreach (object?[] row in Query(connection, """
            SELECT machine, agent, max(time)
            FROM events
            GROUP BY machine, agent
            ORDER BY machine, agent
            """))
        {
            DateTimeOffset last = AsUtc(row[2]);
            double daysSilent = (now - last).TotalDays;
            tiles.Add(new LastSeenTile(
                (string)row[0]!, (string)row[1]!, last,
                Math.Round(daysSilent, 1),
                daysSilent > DeadManThresholdDays ? "red" : "ok"));
        }
        return tiles;
    }

    /// <summary>
    /// Service sessions in the window — excluded from the practice lenses,
    /// stated on the dashboard per the no-silent-caps rule (ADR-0039).
    /// </summary>
    private static ServiceSessionsSummary ServiceSessions(DuckDBConnection connection, DateTimeOffset now)
    {
        foreach (object?[] row in Query(connection, """
            SELECT count(DISTINCT session),
                   coalesce(string_agg(DISTINCT json_extract_string(data, '$.agent_mode'), ', '), '')
            FROM events_preferred
            WHERE type = 'session.started' AND session IS NOT NULL
              AND json_extract_string(data, '$.agent_mode') LIKE 'service-%'
              AND time >= $cutoff
            """, ("cutoff", now.AddDays(-ThemeWindowDays).UtcDateTime)))
        {
            return new ServiceSessionsSummary(AsLong(row[0]), (string)row[1]!);
        }
        return new ServiceSessionsSummary(0, "");
    }

    private static List<ReadsByLayerRow> ReadsByLayer(DuckDBConnection connection, KnowledgeRegistry registry)
    {
        Dictionary<string, KnowledgeSource> sourcesById = registry.Sources.ToDictionary(source => source.Id);

        Dictionary<(string Date, string Layer), long> reads = new();
        foreach (object?[] row in Query(connection, """
            SELECT strftime(date_trunc('day', time), '%Y-%m-%d') AS day, subject, count(*)
            FROM practice_events
            WHERE type IN ('knowledge.read', 'context.loaded') AND subject IS NOT NULL
            GROUP BY day, subject
            """))
        {
            string? sourceId = registry.Resolve((string)row[1]!);
            if (sourceId is null)
            {
                continue;
            }
            string layer = sourcesById[sourceId].Layer.ToString().ToLowerInvariant();
            (string, string) key = ((string)row[0]!, layer);
            reads[key] = reads.GetValueOrDefault(key) + AsLong(row[2]);
        }

        return reads
            .OrderBy(entry => entry.Key.Date, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key.Layer, StringComparer.Ordinal)
            .Select(entry => new ReadsByLayerRow(entry.Key.Date, entry.Key.Layer, entry.Value))
            .ToList();
    }

    private static List<FailedSearchRow> FailedSearches(DuckDBConnection connection)
    {
        List<FailedSearchRow> rows = new();
        foreach (object?[] row in Query(connection, """
            SELECT strftime(date_trunc('day', time), '%Y-%m-%d') AS day,
                   count(*) AS searches,
                   count(*) FILTER (WHERE TRY_CAST(json_extract_string(data, '$.hits') AS BIGINT) = 0) AS zero_hits
            FROM practice_events
            WHERE type = 'knowledge.searched'
              AND TRY_CAST(json_extract_string(data, '$.hits') AS BIGINT) IS NOT NULL
            GROUP BY day
            ORDER BY day
            """))
        {
            long searches = AsLong(row[1]);
            long zeroHits = AsLong(row[2]);
            rows.Add(new FailedSearchRow((string)row[0]!, searches, zeroHits, searches == 0 ? 0 : (double)zeroHits / searches));
        }
        return rows;
    }

    private static List<KbTouchRow> KbTouch(DuckDBConnection connection, HashSet<string> touchedSessions)
    {
        List<KbTouchRow> rows = new();
        foreach (object?[] row in Query(connection, """
            SELECT strftime(date_trunc('day', started_at), '%Y-%m-%d') AS day,
                   list(session) AS sessions
            FROM sessions
            WHERE session NOT IN (SELECT session FROM service_sessions)
            GROUP BY day
            ORDER BY day
            """))
        {
            List<string> daySessions = ((IEnumerable<object>)row[1]!).Cast<string>().ToList();
            long sessions = daySessions.Count;
            long touched = daySessions.Count(touchedSessions.Contains);
            rows.Add(new KbTouchRow((string)row[0]!, sessions, touched, sessions == 0 ? 0 : (double)touched / sessions));
        }
        return rows;
    }

    private static List<TokensRow> Tokens(DuckDBConnection connection)
    {
        List<TokensRow> rows = new();
        foreach (object?[] row in Query(connection, """
            SELECT strftime(date_trunc('day', started_at), '%Y-%m-%d') AS day,
                   coalesce(sum(input_tokens), 0),
                   coalesce(sum(cache_read_tokens), 0)
            FROM sessions
            GROUP BY day
            ORDER BY day
            """))
        {
            rows.Add(new TokensRow((string)row[0]!, AsLong(row[1]), AsLong(row[2])));
        }
        return rows;
    }

    private static (List<ThemeReadsRow> Read, List<ThemeReadsRow> Unused) ReadsByTheme(
        DuckDBConnection connection, KnowledgeRegistry registry, DateTimeOffset now)
    {
        Dictionary<string, KnowledgeSource> sourcesById = registry.Sources.ToDictionary(source => source.Id);

        Dictionary<(string Source, string Theme), long> reads = new();
        foreach (object?[] row in Query(connection, """
            SELECT subject, count(*)
            FROM practice_events
            WHERE type IN ('knowledge.read', 'context.loaded')
              AND subject IS NOT NULL
              AND time >= $cutoff
            GROUP BY subject
            """, ("cutoff", now.AddDays(-ThemeWindowDays).UtcDateTime)))
        {
            string subject = (string)row[0]!;
            string? sourceId = registry.Resolve(subject);
            if (sourceId is null)
            {
                continue;
            }
            (string, string) key = (sourceId, ThemeOf(sourcesById[sourceId], subject));
            reads[key] = reads.GetValueOrDefault(key) + AsLong(row[1]);
        }

        Dictionary<(string Source, string Theme), long> notes = new();
        foreach (InventoryNote note in NoteInventory.Scan(registry))
        {
            (string, string) key = (note.SourceId, ThemeOf(sourcesById[note.SourceId], note.Path));
            notes[key] = notes.GetValueOrDefault(key) + 1;
        }

        List<ThemeReadsRow> rows = reads.Keys.Union(notes.Keys)
            .Select(key => new ThemeReadsRow(
                key.Theme.Length == 0 ? key.Source : $"{key.Source}/{key.Theme}",
                key.Source,
                reads.GetValueOrDefault(key),
                notes.GetValueOrDefault(key)))
            .ToList();
        return (
            rows.Where(row => row.Reads > 0)
                .OrderByDescending(row => row.Reads).ThenBy(row => row.Theme, StringComparer.Ordinal)
                .Take(ThemeChartLimit).ToList(),
            rows.Where(row => row.Reads == 0)
                .OrderByDescending(row => row.Notes).ThenBy(row => row.Theme, StringComparer.Ordinal)
                .ToList());
    }

    private static string ThemeOf(KnowledgeSource source, string path)
    {
        string relative = Path.GetRelativePath(source.Root, path);
        int separator = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        return separator < 0 ? "" : relative[..separator];
    }

    private static IEnumerable<object?[]> Query(DuckDBConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.Add(new DuckDBParameter(name, value));
        }
        using DuckDBDataReader reader = (DuckDBDataReader)command.ExecuteReader();
        while (reader.Read())
        {
            object?[] values = new object?[reader.FieldCount];
            for (int index = 0; index < reader.FieldCount; index++)
            {
                values[index] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }
            yield return values;
        }
    }

    private static long AsLong(object? value)
    {
        if (value is BigInteger bigInteger)
        {
            return (long)bigInteger;
        }
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset AsUtc(object? value)
    {
        DateTime dateTime = (DateTime)value!;
        return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
    }
}
