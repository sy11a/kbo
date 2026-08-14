using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Adapters.Opencode;
using Kbo.Registry;
using Kbo.Schemas;
using Microsoft.Data.Sqlite;

namespace Kbo.Tests;

public class OpencodeMinerTests : IDisposable
{
    private readonly string workspace;
    private readonly string vaultRoot;
    private readonly string databasePath;
    private readonly KnowledgeRegistry registry;

    private static readonly long BaseMs = DateTimeOffset.Parse("2026-07-15T10:00:00Z", CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();

    public OpencodeMinerTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-oc-miner-tests").FullName;
        vaultRoot = Path.Combine(workspace, "Knowledge");
        Directory.CreateDirectory(vaultRoot);
        databasePath = Path.Combine(workspace, "opencode.db");
        registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {vaultRoot}
            """);
        SeedDatabase();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(workspace, recursive: true);
    }

    private void SeedDatabase()
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE session (
                id TEXT PRIMARY KEY, directory TEXT NOT NULL, agent TEXT, model TEXT,
                tokens_input INTEGER DEFAULT 0, tokens_output INTEGER DEFAULT 0,
                tokens_cache_read INTEGER DEFAULT 0, time_created INTEGER NOT NULL, time_updated INTEGER NOT NULL);
            CREATE TABLE part (
                id TEXT PRIMARY KEY, message_id TEXT, session_id TEXT NOT NULL,
                time_created INTEGER NOT NULL, time_updated INTEGER NOT NULL, data TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();

        Insert(connection, "INSERT INTO session VALUES ('ses_a', @dir, 'build', @model, 1200, 300, 900000, @t0, @t0)",
            ("@dir", workspace),
            ("@model", """{"id":"glm-5.1","providerID":"zai"}"""),
            ("@t0", BaseMs));

        JsonObject readPart = new()
        {
            ["type"] = "tool",
            ["tool"] = "read",
            ["callID"] = "call-1",
            ["state"] = new JsonObject
            {
                ["status"] = "completed",
                ["input"] = new JsonObject { ["filePath"] = Path.Combine(vaultRoot, "note.md") },
                ["time"] = new JsonObject { ["start"] = BaseMs + 60_000, ["end"] = BaseMs + 60_100 },
            },
        };
        JsonObject grepPart = new()
        {
            ["type"] = "tool",
            ["tool"] = "grep",
            ["callID"] = "call-2",
            ["state"] = new JsonObject
            {
                ["status"] = "completed",
                ["input"] = new JsonObject { ["pattern"] = "duckdb", ["path"] = vaultRoot },
                ["metadata"] = new JsonObject { ["matches"] = 8, ["truncated"] = false },
                ["time"] = new JsonObject { ["start"] = BaseMs + 120_000 },
            },
        };
        JsonObject textPart = new() { ["type"] = "text", ["text"] = "hello" };
        Insert(connection, "INSERT INTO part VALUES ('prt_1','msg_1','ses_a',@t,@t,@data)", ("@t", BaseMs + 60_000), ("@data", readPart.ToJsonString()));
        Insert(connection, "INSERT INTO part VALUES ('prt_2','msg_1','ses_a',@t,@t,@data)", ("@t", BaseMs + 120_000), ("@data", grepPart.ToJsonString()));
        Insert(connection, "INSERT INTO part VALUES ('prt_3','msg_1','ses_a',@t,@t,@data)", ("@t", BaseMs + 130_000), ("@data", textPart.ToJsonString()));

        JsonObject skillPart = new()
        {
            ["type"] = "tool",
            ["tool"] = "skill",
            ["callID"] = "call-3",
            ["state"] = new JsonObject
            {
                ["status"] = "completed",
                ["input"] = new JsonObject { ["name"] = "grilling" },
                ["metadata"] = new JsonObject { ["name"] = "grilling", ["dir"] = "/skills/grilling", ["truncated"] = false },
                ["time"] = new JsonObject { ["start"] = BaseMs + 140_000, ["end"] = BaseMs + 140_050 },
            },
        };
        JsonObject namelessSkillPart = new()
        {
            ["type"] = "tool",
            ["tool"] = "skill",
            ["callID"] = "call-4",
            ["state"] = new JsonObject
            {
                ["status"] = "completed",
                ["input"] = new JsonObject(),
                ["time"] = new JsonObject { ["start"] = BaseMs + 150_000 },
            },
        };
        Insert(connection, "INSERT INTO part VALUES ('prt_4','msg_1','ses_a',@t,@t,@data)", ("@t", BaseMs + 140_000), ("@data", skillPart.ToJsonString()));
        Insert(connection, "INSERT INTO part VALUES ('prt_5','msg_1','ses_a',@t,@t,@data)", ("@t", BaseMs + 150_000), ("@data", namelessSkillPart.ToJsonString()));
    }

    private static void Insert(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Mine_EmitsSessionStartedWithAggregatedUsageAndModelId()
    {
        List<JsonObject> events = OpencodeMiner.Mine(databasePath, new[] { "ses_a" }, registry, new Random(42));

        EventValidator validator = new();
        Assert.All(events, e => Assert.True(validator.Validate(e.ToJsonString()).IsValid,
            string.Join("; ", validator.Validate(e.ToJsonString()).Errors)));

        JsonObject started = events.Single(e => (string?)e["type"] == "session.started");
        Assert.Equal("ses_a", (string?)started["session"]);
        Assert.Equal("glm-5.1", (string?)started["model"]);
        Assert.Equal("2026-07-15T10:00:00Z", (string?)started["time"]);
        Assert.Equal("harvest", (string?)started["data"]!["origin"]);
        Assert.Equal("ses_a", (string?)started["data"]!["transcript"]);
        JsonObject usage = started["data"]!["usage"]!.AsObject();
        Assert.Equal(1200, (long?)usage["input_tokens"]);
        Assert.Equal(900000, (long?)usage["cache_read_tokens"]);
        Assert.Equal(300, (long?)usage["output_tokens"]);
        Assert.Null(started["data"]!["branch"]);
    }

    [Fact]
    public void Mine_EmitsToolEvents_WithAuthoritativeHitsAndPartTimes()
    {
        List<JsonObject> events = OpencodeMiner.Mine(databasePath, new[] { "ses_a" }, registry, new Random(42));

        Assert.Equal(new[] { "session.started", "knowledge.read", "knowledge.searched", "skill.invoked" },
            events.Select(e => (string?)e["type"]).ToArray());

        JsonObject read = events[1];
        Assert.Equal("vault", (string?)read["kbroot"]);
        Assert.Equal("glm-5.1", (string?)read["model"]);
        Assert.Equal("2026-07-15T10:01:00Z", (string?)read["time"]);

        JsonObject searched = events[2];
        Assert.Equal(8, (int?)searched["data"]!["hits"]);
        Assert.Equal("vault", (string?)searched["kbroot"]);
    }

    [Fact]
    public void Mine_EmitsSkillInvoked_FromSkillTool_SkippingNamelessOnes()
    {
        List<JsonObject> events = OpencodeMiner.Mine(databasePath, new[] { "ses_a" }, registry, new Random(42));

        JsonObject skill = events.Single(e => (string?)e["type"] == "skill.invoked");
        Assert.Equal("grilling", (string?)skill["subject"]);
        Assert.Equal("grilling", (string?)skill["data"]!["skill"]);
        Assert.Null(skill["kbroot"]);
        Assert.Equal("2026-07-15T10:02:20Z", (string?)skill["time"]);
        Assert.Equal("harvest", (string?)skill["data"]!["origin"]);
        Assert.Equal("ses_a", (string?)skill["data"]!["transcript"]);
        Assert.Equal("skill", (string?)skill["data"]!["raw"]!["tool"]);
    }

    [Fact]
    public void Mine_UnrequestedSessions_AreNotMined()
    {
        Assert.Empty(OpencodeMiner.Mine(databasePath, Array.Empty<string>(), registry, new Random(42)));
    }

    [Fact]
    public void EnumerateSessionIds_ListsAllSessions()
    {
        IReadOnlyList<string> ids = OpencodeMiner.EnumerateSessionIds(databasePath);

        Assert.Equal(new[] { "ses_a" }, ids);
    }
}
