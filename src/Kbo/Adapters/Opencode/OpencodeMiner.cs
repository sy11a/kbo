using System.Text.Json;
using System.Text.Json.Nodes;
using Kbo.Adapters.ClaudeCode;
using Kbo.Registry;
using Kbo.Schemas;
using Microsoft.Data.Sqlite;

namespace Kbo.Adapters.Opencode;

/// <summary>
/// Mines the opencode SQLite session store (read-only) into envelope events —
/// the opencode counterpart of the Claude Code TranscriptMiner (ADR-0014).
/// The session row pre-aggregates usage; parts carry tool activity with
/// authoritative hit counts in state.metadata.
/// </summary>
public static class OpencodeMiner
{
    public static IReadOnlyList<string> EnumerateSessionIds(string databasePath)
    {
        List<string> ids = new();
        if (!File.Exists(databasePath))
        {
            return ids;
        }
        using SqliteConnection connection = OpenReadOnly(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM session ORDER BY id";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }
        return ids;
    }

    public static List<JsonObject> Mine(
        string databasePath,
        IReadOnlyCollection<string> sessionIds,
        KnowledgeRegistry registry,
        Random random)
    {
        List<JsonObject> events = new();
        if (sessionIds.Count == 0 || !File.Exists(databasePath))
        {
            return events;
        }
        HashSet<string> wanted = new(sessionIds);

        using SqliteConnection connection = OpenReadOnly(databasePath);
        using SqliteCommand sessionCommand = connection.CreateCommand();
        sessionCommand.CommandText = """
            SELECT id, directory, agent, model, tokens_input, tokens_output, tokens_cache_read, time_created
            FROM session ORDER BY time_created, id
            """;
        using SqliteDataReader sessionReader = sessionCommand.ExecuteReader();
        while (sessionReader.Read())
        {
            string sessionId = sessionReader.GetString(0);
            if (!wanted.Contains(sessionId))
            {
                continue;
            }

            string? directory = sessionReader.IsDBNull(1) ? null : sessionReader.GetString(1);
            string? model = ModelId(sessionReader.IsDBNull(3) ? null : sessionReader.GetString(3));
            DateTimeOffset started = DateTimeOffset.FromUnixTimeMilliseconds(sessionReader.GetInt64(7));
            GitContext git = GitContext.Discover(directory);
            string? repo = git.RepoRoot ?? directory;

            JsonObject sessionData = new()
            {
                [EventDataFields.Branch] = null,
                [EventDataFields.Usage] = new JsonObject
                {
                    ["input_tokens"] = sessionReader.GetInt64(4),
                    ["cache_read_tokens"] = sessionReader.GetInt64(6),
                    ["output_tokens"] = sessionReader.GetInt64(5),
                },
                [EventDataFields.Raw] = new JsonObject
                {
                    ["session_id"] = sessionId,
                    ["directory"] = directory,
                    ["agent_mode"] = sessionReader.IsDBNull(2) ? null : sessionReader.GetString(2),
                },
                [EventDataFields.Origin] = EventDataFields.OriginHarvest,
                [EventDataFields.Transcript] = sessionId,
            };
            events.Add(EventEnvelope.Create(
                EventTypes.SessionStarted, sessionId, null, sessionData,
                registry.Machine, OpencodeRetention.AgentName, sessionId, repo, task: null, model, started, random));

            events.AddRange(MineParts(connection, sessionId, directory, repo, model, registry, random));
        }

        return events;
    }

    private static List<JsonObject> MineParts(
        SqliteConnection connection,
        string sessionId,
        string? directory,
        string? repo,
        string? model,
        KnowledgeRegistry registry,
        Random random)
    {
        List<JsonObject> events = new();
        using SqliteCommand partCommand = connection.CreateCommand();
        partCommand.CommandText = "SELECT data FROM part WHERE session_id = @session ORDER BY time_created, id";
        partCommand.Parameters.AddWithValue("@session", sessionId);
        using SqliteDataReader reader = partCommand.ExecuteReader();
        while (reader.Read())
        {
            JsonObject? part;
            try
            {
                part = JsonNode.Parse(reader.GetString(0)) as JsonObject;
            }
            catch (JsonException)
            {
                continue;
            }
            if (part is null
                || (string?)part["type"] != "tool"
                || (string?)part["tool"] is not string tool
                || part["state"] is not JsonObject state
                || state["input"] is not JsonObject input
                || state["time"]?["start"] is not JsonValue startValue
                || !startValue.TryGetValue(out long startMs))
            {
                continue;
            }

            DateTimeOffset time = DateTimeOffset.FromUnixTimeMilliseconds(startMs);
            JsonObject raw = new()
            {
                ["session_id"] = sessionId,
                ["tool"] = tool,
                ["call_id"] = (string?)part["callID"],
                ["input"] = input.DeepClone(),
            };

            MappedTool? mapped = tool switch
            {
                OpencodeAdapter.Tools.Read => MapRead(input, directory, raw, registry),
                OpencodeAdapter.Tools.Grep or OpencodeAdapter.Tools.Glob => MapSearch(input, state, directory, raw, registry),
                OpencodeAdapter.Tools.Write or OpencodeAdapter.Tools.Edit => MapWrite(input, directory, raw, registry),
                _ => null,
            };
            if (mapped is null)
            {
                continue;
            }

            mapped.Data[EventDataFields.Origin] = EventDataFields.OriginHarvest;
            mapped.Data[EventDataFields.Transcript] = sessionId;
            events.Add(EventEnvelope.Create(
                mapped.Type, mapped.Subject, mapped.Kbroot, mapped.Data,
                registry.Machine, OpencodeRetention.AgentName, sessionId, repo, task: null, model, time, random));
        }
        return events;
    }

    private sealed record MappedTool(string Type, string? Subject, string? Kbroot, JsonObject Data);

    private static MappedTool? MapRead(JsonObject input, string? directory, JsonObject raw, KnowledgeRegistry registry)
    {
        string? filePath = ClaudeCodeAdapter.AbsolutePath((string?)input[OpencodeAdapter.Payload.FilePath], directory);
        if (filePath is null)
        {
            return null;
        }
        return new MappedTool(EventTypes.KnowledgeRead, filePath, registry.Resolve(filePath), new JsonObject
        {
            [EventDataFields.Path] = filePath,
            [EventDataFields.ContentHash] = null,
            [EventDataFields.Raw] = raw,
        });
    }

    private static MappedTool? MapSearch(JsonObject input, JsonObject state, string? directory, JsonObject raw, KnowledgeRegistry registry)
    {
        string? pattern = (string?)input[OpencodeAdapter.Payload.Pattern];
        if (pattern is null)
        {
            return null;
        }
        string? root = ClaudeCodeAdapter.AbsolutePath((string?)input[OpencodeAdapter.Payload.Path], directory)
            ?? ClaudeCodeAdapter.AbsolutePath(directory, null);
        int? hits = null;
        if (state["metadata"] is JsonObject metadata)
        {
            foreach (string key in new[] { "matches", "count" })
            {
                if (metadata[key] is JsonValue value && value.TryGetValue(out int parsed))
                {
                    hits = parsed;
                    break;
                }
            }
        }
        return new MappedTool(EventTypes.KnowledgeSearched, pattern, root is null ? null : registry.Resolve(root), new JsonObject
        {
            [EventDataFields.Pattern] = pattern,
            [EventDataFields.Root] = root,
            [EventDataFields.Hits] = hits,
            [EventDataFields.Raw] = raw,
        });
    }

    private static MappedTool? MapWrite(JsonObject input, string? directory, JsonObject raw, KnowledgeRegistry registry)
    {
        string? filePath = ClaudeCodeAdapter.AbsolutePath((string?)input[OpencodeAdapter.Payload.FilePath], directory);
        if (filePath is null)
        {
            return null;
        }
        return new MappedTool(EventTypes.KnowledgeWritten, filePath, registry.Resolve(filePath), new JsonObject
        {
            [EventDataFields.Path] = filePath,
            [EventDataFields.Raw] = raw,
        });
    }

    private static string? ModelId(string? modelJson)
    {
        if (modelJson is null)
        {
            return null;
        }
        try
        {
            return (string?)(JsonNode.Parse(modelJson)?["id"]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SqliteConnection OpenReadOnly(string databasePath)
    {
        SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly;Pooling=false");
        connection.Open();
        return connection;
    }
}
