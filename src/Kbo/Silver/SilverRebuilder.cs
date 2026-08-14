using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DuckDB.NET.Data;
using Kbo.Schemas;

namespace Kbo.Silver;

public sealed record RebuildResult(long EventCount, long SessionCount, long SkippedLines);

public static class SilverRebuilder
{
    private const string CreateEventsTable = """
        CREATE TABLE events (
            specversion VARCHAR,
            id VARCHAR NOT NULL,
            source VARCHAR,
            type VARCHAR NOT NULL,
            time TIMESTAMP NOT NULL,
            subject VARCHAR,
            machine VARCHAR,
            agent VARCHAR,
            session VARCHAR,
            repo VARCHAR,
            task VARCHAR,
            model VARCHAR,
            kbroot VARCHAR,
            schemaref VARCHAR,
            origin VARCHAR,
            transcript VARCHAR,
            data VARCHAR NOT NULL
        )
        """;

    private const string CreateEventsPreferredView = """
        CREATE VIEW events_preferred AS
        SELECT events.* FROM events
        LEFT JOIN (
            SELECT session, max(time) AS harvested_until
            FROM events
            WHERE origin = 'harvest' AND session IS NOT NULL
            GROUP BY session
        ) harvested ON events.session = harvested.session
        WHERE events.origin = 'harvest'
           OR events.type = 'context.loaded'
           OR harvested.session IS NULL
           OR events.time > harvested.harvested_until
        """;

    private const string CreateSessionsView = """
        CREATE VIEW sessions AS
        SELECT
            session,
            machine,
            agent,
            min(time) AS started_at,
            arg_min(model, time) FILTER (WHERE model IS NOT NULL) AS model,
            arg_min(json_extract_string(data, '$.branch'), time)
                FILTER (WHERE json_extract_string(data, '$.branch') IS NOT NULL) AS branch,
            arg_min(task, time) FILTER (WHERE task IS NOT NULL) AS task,
            arg_min(repo, time) FILTER (WHERE repo IS NOT NULL) AS repo,
            sum(CAST(json_extract_string(data, '$.usage.input_tokens') AS BIGINT)) AS input_tokens,
            sum(CAST(json_extract_string(data, '$.usage.cache_read_tokens') AS BIGINT)) AS cache_read_tokens,
            sum(CAST(json_extract_string(data, '$.usage.output_tokens') AS BIGINT)) AS output_tokens,
            count(*) AS transcript_count
        FROM events_preferred
        WHERE type = 'session.started'
        GROUP BY session, machine, agent
        """;

    public static RebuildResult Rebuild(string eventsRepoRoot, string silverPath)
    {
        if (File.Exists(silverPath))
        {
            File.Delete(silverPath);
        }
        string? silverDirectory = Path.GetDirectoryName(silverPath);
        if (!string.IsNullOrEmpty(silverDirectory))
        {
            Directory.CreateDirectory(silverDirectory);
        }

        using DuckDBConnection connection = new($"Data Source={silverPath}");
        connection.Open();
        Execute(connection, CreateEventsTable);

        long eventCount = 0;
        long skippedLines = 0;
        string bronzeRoot = Path.Combine(eventsRepoRoot, "bronze");
        if (Directory.Exists(bronzeRoot))
        {
            using DuckDBTransaction transaction = connection.BeginTransaction();
            using DuckDBCommand insert = CreateInsertCommand(connection);
            foreach (string monthFile in Directory
                .EnumerateFiles(bronzeRoot, "*.ndjsonl", SearchOption.AllDirectories)
                .Order())
            {
                foreach (string line in File.ReadLines(monthFile))
                {
                    if (InsertEvent(insert, line))
                    {
                        eventCount++;
                    }
                    else
                    {
                        skippedLines++;
                    }
                }
            }
            transaction.Commit();
        }

        Execute(connection, CreateEventsPreferredView);
        Execute(connection, CreateSessionsView);

        using DuckDBCommand sessionCountCommand = connection.CreateCommand();
        sessionCountCommand.CommandText = "SELECT count(*) FROM sessions";
        long sessionCount = Convert.ToInt64(sessionCountCommand.ExecuteScalar(), CultureInfo.InvariantCulture);

        return new RebuildResult(eventCount, sessionCount, skippedLines);
    }

    private const int InsertColumnCount = 17;

    private static DuckDBCommand CreateInsertCommand(DuckDBConnection connection)
    {
        DuckDBCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO events VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
        for (int position = 0; position < InsertColumnCount; position++)
        {
            insert.Parameters.Add(new DuckDBParameter());
        }
        return insert;
    }

    private static bool InsertEvent(DuckDBCommand insert, string bronzeLine)
    {
        JsonObject? envelopeEvent;
        try
        {
            envelopeEvent = JsonNode.Parse(bronzeLine) as JsonObject;
        }
        catch (JsonException)
        {
            return false;
        }
        if (envelopeEvent is null)
        {
            return false;
        }

        string? time = (string?)envelopeEvent[EnvelopeFields.Time];
        if (time is null
            || (string?)envelopeEvent[EnvelopeFields.Id] is null
            || !DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsedTime))
        {
            return false;
        }

        JsonNode? data = envelopeEvent[EnvelopeFields.Data];

        int position = 0;
        SetParameter(insert, ref position, (string?)envelopeEvent[EnvelopeFields.SpecVersion]);
        SetParameter(insert, ref position, (string?)envelopeEvent[EnvelopeFields.Id]);
        SetParameter(insert, ref position, (string?)envelopeEvent[EnvelopeFields.Source]);
        SetParameter(insert, ref position, (string?)envelopeEvent[EnvelopeFields.Type]);
        SetParameter(insert, ref position, parsedTime.UtcDateTime);
        SetParameter(insert, ref position, (string?)envelopeEvent[EnvelopeFields.Subject]);
        SetParameter(insert, ref position, (string?)envelopeEvent[EnvelopeFields.Machine]);
        SetParameter(insert, ref position, (string?)envelopeEvent[EnvelopeFields.Agent]);
        SetParameter(insert, ref position, (string?)envelopeEvent[EnvelopeFields.Session]);
        SetParameter(insert, ref position, (string?)envelopeEvent[EnvelopeFields.Repo]);
        SetParameter(insert, ref position, (string?)envelopeEvent[EnvelopeFields.Task]);
        SetParameter(insert, ref position, (string?)envelopeEvent[EnvelopeFields.Model]);
        SetParameter(insert, ref position, (string?)envelopeEvent[EnvelopeFields.Kbroot]);
        SetParameter(insert, ref position, (string?)envelopeEvent[EnvelopeFields.SchemaRef]);
        SetParameter(insert, ref position, (string?)data?[EventDataFields.Origin]);
        SetParameter(insert, ref position, (string?)data?[EventDataFields.Transcript]);
        SetParameter(insert, ref position, data?.ToJsonString() ?? "{}");
        insert.ExecuteNonQuery();
        return true;
    }

    private static void SetParameter(DuckDBCommand command, ref int position, object? value)
    {
        command.Parameters[position].Value = value ?? DBNull.Value;
        position++;
    }

    private static void Execute(DuckDBConnection connection, string sql)
    {
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
