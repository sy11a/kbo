using System.Globalization;
using System.Numerics;
using DuckDB.NET.Data;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Gold;

/// <summary>
/// Per-day activity digests from silver, computed once (P2). One DayDigest per
/// day with any session, read, or search within the window. Knowledge is
/// classified registry-now (ADR-0021).
/// </summary>
public static class DailyDigestComputer
{
    public const int WindowDays = 90;
    public const int TopZeroHitQueryCap = 10;

    public static IReadOnlyList<DayDigest> Compute(string silverPath, KnowledgeRegistry registry, TimeProvider clock)
    {
        DateTime cutoff = clock.GetUtcNow().AddDays(-WindowDays).UtcDateTime;
        using DuckDBConnection connection = new($"Data Source={silverPath}");
        connection.Open();

        Dictionary<string, KnowledgeSource> sourcesById = registry.Sources.ToDictionary(source => source.Id);
        HashSet<string> registeredIds = sourcesById.Keys.ToHashSet();
        HashSet<string> touchedSessions = TouchedSessions(connection, registry, registeredIds);

        Dictionary<string, long[]> countsBySession = SessionEventCounts(connection, cutoff);

        SortedDictionary<string, DayBuilder> days = new(StringComparer.Ordinal);
        DayBuilder Day(string date) => days.TryGetValue(date, out DayBuilder? existing)
            ? existing
            : days[date] = new DayBuilder();

        foreach (object?[] row in Query(connection, """
            SELECT session, agent, coalesce(repo, '(unknown)') AS repo, started_at,
                   coalesce(input_tokens, 0), coalesce(cache_read_tokens, 0)
            FROM sessions
            WHERE started_at >= $cutoff
            """, ("cutoff", cutoff)))
        {
            string session = (string)row[0]!;
            string agent = (string)row[1]!;
            string repo = (string)row[2]!;
            DateTime started = (DateTime)row[3]!;
            long input = AsLong(row[4]);
            long cache = AsLong(row[5]);
            bool touched = touchedSessions.Contains(session);
            long[] counts = countsBySession.GetValueOrDefault(session) ?? new long[4];

            DayBuilder day = Day(started.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            day.Sessions++;
            if (touched)
            {
                day.Touched++;
            }
            day.ByAgent[agent] = day.ByAgent.GetValueOrDefault(agent) + 1;
            day.ByRepo[repo] = day.ByRepo.GetValueOrDefault(repo) + 1;
            day.InputTokens += input;
            day.CacheReadTokens += cache;
            day.SessionRows.Add(new DaySession(
                started.ToString("HH:mm", CultureInfo.InvariantCulture), agent, repo,
                counts[0], counts[1], counts[2], counts[3], touched, input, cache));
        }

        foreach (object?[] row in Query(connection, """
            SELECT strftime(date_trunc('day', time), '%Y-%m-%d') AS day, subject, count(*)
            FROM events_preferred
            WHERE type IN ('knowledge.read', 'context.loaded') AND subject IS NOT NULL AND time >= $cutoff
            GROUP BY day, subject
            """, ("cutoff", cutoff)))
        {
            string? sourceId = registry.Resolve((string)row[1]!);
            if (sourceId is null)
            {
                continue;
            }
            DayBuilder day = Day((string)row[0]!);
            long count = AsLong(row[2]);
            string layer = sourcesById[sourceId].Layer.ToString().ToLowerInvariant();
            day.ReadsByLayer[layer] = day.ReadsByLayer.GetValueOrDefault(layer) + count;
            day.TotalReads += count;
        }

        foreach (object?[] row in Query(connection, """
            SELECT strftime(date_trunc('day', time), '%Y-%m-%d') AS day,
                   subject,
                   TRY_CAST(json_extract_string(data, '$.hits') AS BIGINT) AS hits
            FROM events_preferred
            WHERE type = 'knowledge.searched' AND time >= $cutoff
              AND TRY_CAST(json_extract_string(data, '$.hits') AS BIGINT) IS NOT NULL
            """, ("cutoff", cutoff)))
        {
            DayBuilder day = Day((string)row[0]!);
            long hits = AsLong(row[2]);
            day.Searches++;
            if (hits == 0)
            {
                day.ZeroHits++;
                string query = row[1] as string ?? "(empty)";
                day.ZeroHitQueries[query] = day.ZeroHitQueries.GetValueOrDefault(query) + 1;
            }
            else
            {
                day.Hits++;
            }
        }

        foreach (object?[] row in Query(connection, """
            SELECT strftime(date_trunc('day', time), '%Y-%m-%d') AS day,
                   json_extract_string(data, '$.skill') AS skill, count(*)
            FROM events_preferred
            WHERE type = 'skill.invoked' AND time >= $cutoff
              AND json_extract_string(data, '$.skill') IS NOT NULL
            GROUP BY day, skill
            """, ("cutoff", cutoff)))
        {
            DayBuilder day = Day((string)row[0]!);
            day.Skills[(string)row[1]!] = day.Skills.GetValueOrDefault((string)row[1]!) + AsLong(row[2]);
        }

        return days
            .OrderByDescending(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => entry.Value.Build(entry.Key))
            .ToList();
    }

    private static Dictionary<string, long[]> SessionEventCounts(DuckDBConnection connection, DateTime cutoff)
    {
        Dictionary<string, long[]> counts = new();
        foreach (object?[] row in Query(connection, """
            SELECT session, type, count(*)
            FROM events_preferred
            WHERE session IS NOT NULL AND time >= $cutoff
              AND type IN ('knowledge.read', 'knowledge.searched', 'skill.invoked', 'knowledge.written')
            GROUP BY session, type
            """, ("cutoff", cutoff)))
        {
            string session = (string)row[0]!;
            long[] slot = counts.TryGetValue(session, out long[]? existing) ? existing : counts[session] = new long[4];
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
        return counts;
    }

    private static HashSet<string> TouchedSessions(DuckDBConnection connection, KnowledgeRegistry registry, HashSet<string> registeredIds)
    {
        HashSet<string> touched = new();
        foreach (object?[] row in Query(connection, """
            SELECT DISTINCT session, subject, kbroot
            FROM events_preferred
            WHERE session IS NOT NULL AND (subject IS NOT NULL OR kbroot IS NOT NULL)
            """))
        {
            string session = (string)row[0]!;
            if (touched.Contains(session))
            {
                continue;
            }
            bool resolvesNow = row[1] is string subject && registry.Resolve(subject) is not null;
            bool stampStillRegistered = row[2] is string kbroot && registeredIds.Contains(kbroot);
            if (resolvesNow || stampStillRegistered)
            {
                touched.Add(session);
            }
        }
        return touched;
    }

    private sealed class DayBuilder
    {
        public long Sessions;
        public long Touched;
        public long TotalReads;
        public long Searches;
        public long Hits;
        public long ZeroHits;
        public long InputTokens;
        public long CacheReadTokens;
        public Dictionary<string, long> ByAgent { get; } = new();
        public Dictionary<string, long> ByRepo { get; } = new();
        public Dictionary<string, long> ReadsByLayer { get; } = new();
        public Dictionary<string, long> ZeroHitQueries { get; } = new();
        public Dictionary<string, long> Skills { get; } = new();
        public List<DaySession> SessionRows { get; } = new();

        public DayDigest Build(string date)
        {
            return new DayDigest(
                date,
                Sessions,
                Touched,
                Sessions == 0 ? 0 : (double)Touched / Sessions,
                Rank(ByAgent),
                Rank(ByRepo),
                Rank(ReadsByLayer),
                TotalReads,
                Searches,
                Hits,
                ZeroHits,
                Rank(ZeroHitQueries).Take(TopZeroHitQueryCap).ToList(),
                InputTokens,
                CacheReadTokens,
                Rank(Skills),
                SessionRows.OrderByDescending(session => session.Time, StringComparer.Ordinal).ToList());
        }

        private static List<DayCount> Rank(Dictionary<string, long> counts)
        {
            return counts
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new DayCount(entry.Key, entry.Value))
                .ToList();
        }
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
}
