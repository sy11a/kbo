using System.Text.Json.Nodes;
using DuckDB.NET.Data;
using Kbo.Bronze;
using Kbo.Silver;

namespace Kbo.Tests;

public class SilverRebuilderTests : IDisposable
{
    private readonly string workspace;
    private readonly string eventsRepo;
    private readonly string silverPath;

    public SilverRebuilderTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-silver-tests").FullName;
        eventsRepo = Path.Combine(workspace, "kb-events");
        silverPath = Path.Combine(workspace, "silver.duckdb");
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private static JsonObject Event(
        string id, string type, string time, string? session, string? origin,
        string? transcript = null, string? subject = null, string? kbroot = null,
        string? model = null, JsonObject? extraData = null)
    {
        JsonObject data = extraData ?? new JsonObject();
        data["origin"] = origin;
        if (transcript is not null)
        {
            data["transcript"] = transcript;
        }
        return new JsonObject
        {
            ["specversion"] = "1.0",
            ["id"] = id,
            ["source"] = "//test-machine/claude-code",
            ["type"] = type,
            ["time"] = time,
            ["subject"] = subject,
            ["data"] = data,
            ["machine"] = "test-machine",
            ["agent"] = "claude-code",
            ["session"] = session,
            ["repo"] = null,
            ["task"] = null,
            ["model"] = model,
            ["kbroot"] = kbroot,
            ["schemaref"] = type + "/1",
        };
    }

    private void SeedBronze()
    {
        new BronzeStore(eventsRepo).Append(new[]
        {
            Event("01A00000000000000000000001", "session.started", "2026-07-01T10:00:00Z", "sess-mixed", "hook"),
            Event("01A00000000000000000000002", "knowledge.read", "2026-07-01T10:01:00Z", "sess-mixed", "hook",
                subject: "/kb/note.md", kbroot: "vault"),
            Event("01A00000000000000000000003", "context.loaded", "2026-07-01T10:00:01Z", "sess-mixed", "hook",
                subject: "/repo/CLAUDE.md"),
            Event("01A00000000000000000000004", "session.started", "2026-07-01T10:00:00Z", "sess-mixed", "harvest",
                transcript: "file-1", model: "claude-fable-5",
                extraData: new JsonObject { ["usage"] = new JsonObject { ["input_tokens"] = 100, ["cache_read_tokens"] = 1000, ["output_tokens"] = 10 } }),
            Event("01A00000000000000000000005", "knowledge.read", "2026-07-01T10:01:00Z", "sess-mixed", "harvest",
                transcript: "file-1", subject: "/kb/note.md", kbroot: "vault", model: "claude-fable-5"),
            Event("01A00000000000000000000006", "session.started", "2026-07-01T11:00:00Z", "sess-mixed", "harvest",
                transcript: "file-2", model: "claude-fable-5",
                extraData: new JsonObject { ["usage"] = new JsonObject { ["input_tokens"] = 50, ["cache_read_tokens"] = 500, ["output_tokens"] = 5 } }),
            Event("01A00000000000000000000007", "session.started", "2026-08-01T09:00:00Z", "sess-hook-only", "hook"),
            Event("01A00000000000000000000008", "knowledge.written", "2026-08-01T09:05:00Z", "sess-hook-only", "hook",
                subject: "/kb/new.md", kbroot: "vault"),
        });
    }

    private DuckDBConnection Open()
    {
        DuckDBConnection connection = new($"Data Source={silverPath}");
        connection.Open();
        return connection;
    }

    private long Scalar(DuckDBConnection connection, string sql)
    {
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        if (value is System.Numerics.BigInteger bigInteger)
        {
            return (long)bigInteger;
        }
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Rebuild_LoadsAllBronzeEventsIntoEventsTable()
    {
        SeedBronze();
        SilverRebuilder.Rebuild(eventsRepo, silverPath);

        using DuckDBConnection connection = Open();
        Assert.Equal(8, Scalar(connection, "SELECT count(*) FROM events"));
        Assert.Equal(2, Scalar(connection, "SELECT count(*) FROM events WHERE kbroot = 'vault' AND type = 'knowledge.read'"));
        Assert.Equal(5, Scalar(connection, "SELECT count(*) FROM events WHERE origin = 'hook'"));
        Assert.Equal(2, Scalar(connection, "SELECT count(DISTINCT transcript) FROM events WHERE transcript IS NOT NULL"));
    }

    [Fact]
    public void EventsPreferred_DropsHookRowsOnlyForHarvestCoveredSessions()
    {
        SeedBronze();
        SilverRebuilder.Rebuild(eventsRepo, silverPath);

        using DuckDBConnection connection = Open();
        Assert.Equal(0, Scalar(connection,
            "SELECT count(*) FROM events_preferred WHERE session = 'sess-mixed' AND origin = 'hook' AND type <> 'context.loaded'"));
        Assert.Equal(1, Scalar(connection,
            "SELECT count(*) FROM events_preferred WHERE session = 'sess-mixed' AND type = 'context.loaded'"));
        Assert.Equal(2, Scalar(connection,
            "SELECT count(*) FROM events_preferred WHERE session = 'sess-hook-only'"));
        Assert.Equal(3, Scalar(connection,
            "SELECT count(*) FROM events_preferred WHERE session = 'sess-mixed' AND origin = 'harvest'"));
    }

    [Fact]
    public void EventsPreferred_HookTailAfterLastHarvestEvent_StaysVisible()
    {
        SeedBronze();
        new BronzeStore(eventsRepo).Append(new[]
        {
            Event("01A00000000000000000000009", "knowledge.read", "2026-07-01T12:00:00Z", "sess-mixed", "hook",
                subject: "/kb/tail.md", kbroot: "vault"),
        });
        SilverRebuilder.Rebuild(eventsRepo, silverPath);

        using DuckDBConnection connection = Open();
        Assert.Equal(1, Scalar(connection,
            "SELECT count(*) FROM events_preferred WHERE subject = '/kb/tail.md' AND origin = 'hook'"));
        Assert.Equal(0, Scalar(connection,
            "SELECT count(*) FROM events_preferred WHERE session = 'sess-mixed' AND origin = 'hook' AND type = 'knowledge.read' AND time <= TIMESTAMP '2026-07-01 11:00:00'"));
    }

    [Fact]
    public void Sessions_CollapsesMultiTranscriptSessions_SummingUsage()
    {
        SeedBronze();
        SilverRebuilder.Rebuild(eventsRepo, silverPath);

        using DuckDBConnection connection = Open();
        Assert.Equal(2, Scalar(connection, "SELECT count(*) FROM sessions"));
        Assert.Equal(150, Scalar(connection, "SELECT input_tokens FROM sessions WHERE session = 'sess-mixed'"));
        Assert.Equal(1500, Scalar(connection, "SELECT cache_read_tokens FROM sessions WHERE session = 'sess-mixed'"));
        Assert.Equal(2, Scalar(connection, "SELECT transcript_count FROM sessions WHERE session = 'sess-mixed'"));

        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = "SELECT model, strftime(started_at, '%Y-%m-%dT%H:%M:%SZ') FROM sessions WHERE session = 'sess-mixed'";
        using DuckDB.NET.Data.DuckDBDataReader reader = (DuckDB.NET.Data.DuckDBDataReader)command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("claude-fable-5", reader.GetString(0));
        Assert.Equal("2026-07-01T10:00:00Z", reader.GetString(1));
    }

    [Fact]
    public void Rebuild_IsDeterministic_P3Proof()
    {
        SeedBronze();
        SilverRebuilder.Rebuild(eventsRepo, silverPath);
        List<string> firstDump = DumpEvents();

        File.Delete(silverPath);
        SilverRebuilder.Rebuild(eventsRepo, silverPath);
        List<string> secondDump = DumpEvents();

        Assert.NotEmpty(firstDump);
        Assert.Equal(firstDump, secondDump);
    }

    private List<string> DumpEvents()
    {
        using DuckDBConnection connection = Open();
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, type, time, subject, session, origin, transcript, kbroot, data FROM events ORDER BY id";
        using DuckDB.NET.Data.DuckDBDataReader reader = (DuckDB.NET.Data.DuckDBDataReader)command.ExecuteReader();
        List<string> rows = new();
        while (reader.Read())
        {
            List<string> values = new();
            for (int index = 0; index < reader.FieldCount; index++)
            {
                values.Add(reader.IsDBNull(index) ? "<null>" : reader.GetValue(index).ToString() ?? "<null>");
            }
            rows.Add(string.Join("|", values));
        }
        return rows;
    }
}
