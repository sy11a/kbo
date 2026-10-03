using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Adapters.ClaudeCode;

internal static class TranscriptMiner
{
    private sealed record MinedToolUse(string ToolName, JsonObject Input, string? ToolUseId, DateTimeOffset Time, string? Model, string? Cwd);

    private sealed class MineContext
    {
        public string? SessionId;
        public DateTimeOffset? SessionTime;
        public string? Cwd;
        public string? Branch;
        public string? FirstModel;
        public Dictionary<string, JsonObject> UsageByRequest { get; } = [];
        public Dictionary<string, JsonNode> ResultsByToolUseId { get; } = [];
        public List<MinedToolUse> ToolUses { get; } = [];
    }

    public static List<JsonObject> Mine(
        IEnumerable<string> transcriptLines,
        string transcriptId,
        KnowledgeRegistry registry,
        Random random)
    {
        MineContext context = new();
        foreach (string line in transcriptLines)
        {
            AccumulateLine(line, context);
        }

        if (context.SessionId is null && context.SessionTime is null && context.ToolUses.Count is 0)
        {
            return [];
        }

        string session = context.SessionId ?? transcriptId;
        string? repo = GitContext.Discover(context.Cwd, registry.TaskPattern).RepoRoot ?? context.Cwd;
        string? task = GitContext.TaskFromBranch(context.Branch, registry.TaskPattern);

        List<JsonObject> events = BuildMinedEvents(context, session, repo, task, registry, random);
        StampTranscriptId(events, transcriptId);
        return events;
    }

    private static void AccumulateLine(string line, MineContext context)
    {
        JsonObject? record;
        try
        {
            record = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }
        if (record is null)
        {
            return;
        }

        context.SessionId ??= (string?)record["sessionId"];
        context.Cwd ??= (string?)record["cwd"];
        context.Branch ??= (string?)record["gitBranch"];
        DateTimeOffset? recordTime = ParseTime((string?)record["timestamp"]);
        context.SessionTime ??= recordTime;

        CaptureToolResult(record, context.ResultsByToolUseId);

        if ((string?)record["type"] is not "assistant" || record["message"] is not JsonObject message)
        {
            return;
        }

        string? model = (string?)message["model"];
        context.FirstModel ??= model;

        if (message["usage"] is JsonObject usage && (string?)record["requestId"] is string requestId)
        {
            context.UsageByRequest[requestId] = usage;
        }

        if (message["content"] is not JsonArray content)
        {
            return;
        }
        CollectAssistantToolUses(content, record, model, recordTime, context);
    }

    private static void CaptureToolResult(JsonObject record, Dictionary<string, JsonNode> resultsByToolUseId)
    {
        if (record["toolUseResult"] is not JsonNode toolUseResult || record["message"]?["content"] is not JsonArray resultContent)
        {
            return;
        }
        foreach (JsonNode? contentBlock in resultContent)
        {
            if (contentBlock is JsonObject result
                && (string?)result["type"] is "tool_result"
                && (string?)result["tool_use_id"] is string toolUseId)
            {
                resultsByToolUseId[toolUseId] = toolUseResult;
            }
        }
    }

    private static void CollectAssistantToolUses(JsonArray content, JsonObject record, string? model, DateTimeOffset? recordTime, MineContext context)
    {
        foreach (JsonNode? contentBlock in content)
        {
            if (contentBlock is JsonObject toolUse
                && (string?)toolUse["type"] is "tool_use"
                && (string?)toolUse["name"] is string toolName
                && toolUse["input"] is JsonObject input
                && recordTime is not null)
            {
                context.ToolUses.Add(new MinedToolUse(
                    toolName, input, (string?)toolUse["id"], recordTime.Value, model, (string?)record["cwd"] ?? context.Cwd));
            }
        }
    }

    private static List<JsonObject> BuildMinedEvents(MineContext context, string session, string? repo, string? task, KnowledgeRegistry registry, Random random)
    {
        List<JsonObject> events =
        [
            SessionStartedEvent(session, context.SessionTime, context.Cwd, context.Branch, context.FirstModel, context.UsageByRequest, repo, task, registry, random),
        ];

        foreach (MinedToolUse toolUse in context.ToolUses)
        {
            JsonObject? mapped = MapToolUse(toolUse, session, repo, task, registry, context.ResultsByToolUseId, random);
            if (mapped is not null)
            {
                events.Add(mapped);
            }
        }

        return events;
    }

    private static void StampTranscriptId(List<JsonObject> events, string transcriptId)
    {
        foreach (JsonObject minedEvent in events)
        {
            minedEvent[EnvelopeFields.Data]![EventDataFields.Transcript] = transcriptId;
        }
    }

    private static JsonObject SessionStartedEvent(
        string session,
        DateTimeOffset? sessionTime,
        string? cwd,
        string? branch,
        string? model,
        Dictionary<string, JsonObject> usageByRequest,
        string? repo,
        string? task,
        KnowledgeRegistry registry,
        Random random)
    {
        JsonObject data = new()
        {
            [EventDataFields.Branch] = branch,
            [EventDataFields.Usage] = SumUsage(usageByRequest),
            [EventDataFields.Raw] = new JsonObject
            {
                ["session_id"] = session,
                ["cwd"] = cwd,
                ["gitBranch"] = branch,
            },
            [EventDataFields.Origin] = EventDataFields.OriginHarvest,
        };

        return EventEnvelope.Create(
            EventTypes.SessionStarted,
            subject: session,
            kbroot: null,
            data,
            registry.Machine,
            ClaudeCodeAdapter.AgentName,
            session,
            repo,
            task,
            model,
            sessionTime ?? DateTimeOffset.UnixEpoch,
            random);
    }

    private static JsonObject? MapToolUse(
        MinedToolUse toolUse,
        string session,
        string? repo,
        string? task,
        KnowledgeRegistry registry,
        Dictionary<string, JsonNode> resultsByToolUseId,
        Random random)
    {
        JsonObject raw = new()
        {
            ["tool_name"] = toolUse.ToolName,
            ["tool_input"] = toolUse.Input.DeepClone(),
            ["tool_use_id"] = toolUse.ToolUseId,
            ["session_id"] = session,
        };

        return toolUse.ToolName switch
        {
            HookPayload.Tools.Read => MapReadToolUse(toolUse, raw, session, repo, task, registry, random),
            HookPayload.Tools.Grep or HookPayload.Tools.Glob => MapSearchToolUse(toolUse, raw, session, repo, task, registry, resultsByToolUseId, random),
            HookPayload.Tools.Skill => MapSkillToolUse(toolUse, raw, session, repo, task, registry, random),
            HookPayload.Tools.Write or HookPayload.Tools.Edit or HookPayload.Tools.NotebookEdit => MapWriteToolUse(toolUse, raw, session, repo, task, registry, random),
            _ => null,
        };
    }

    private static JsonObject? MapReadToolUse(
        MinedToolUse toolUse,
        JsonObject raw,
        string session,
        string? repo,
        string? task,
        KnowledgeRegistry registry,
        Random random)
    {
        string? filePath = ClaudeCodeAdapter.AbsolutePath((string?)toolUse.Input[HookPayload.FilePath], toolUse.Cwd);
        if (filePath is null)
        {
            return null;
        }
        JsonObject data = new()
        {
            [EventDataFields.Path] = filePath,
            [EventDataFields.ContentHash] = null,
            [EventDataFields.Raw] = raw,
            [EventDataFields.Origin] = EventDataFields.OriginHarvest,
        };
        return Envelope(EventTypes.KnowledgeRead, filePath, registry.Resolve(filePath), data, toolUse, session, repo, task, registry, random);
    }

    private static JsonObject? MapSearchToolUse(
        MinedToolUse toolUse,
        JsonObject raw,
        string session,
        string? repo,
        string? task,
        KnowledgeRegistry registry,
        Dictionary<string, JsonNode> resultsByToolUseId,
        Random random)
    {
        string? pattern = (string?)toolUse.Input[HookPayload.Pattern];
        if (pattern is null)
        {
            return null;
        }
        string? root = ClaudeCodeAdapter.AbsolutePath((string?)toolUse.Input[HookPayload.Path], toolUse.Cwd)
            ?? ClaudeCodeAdapter.AbsolutePath(toolUse.Cwd, cwd: null);
        JsonNode? toolUseResult = toolUse.ToolUseId is not null
            ? resultsByToolUseId.GetValueOrDefault(toolUse.ToolUseId)
            : null;
        JsonObject data = new()
        {
            [EventDataFields.Pattern] = pattern,
            [EventDataFields.Root] = root,
            [EventDataFields.Hits] = AuthoritativeHits(toolUseResult),
            [EventDataFields.Raw] = raw,
            [EventDataFields.Origin] = EventDataFields.OriginHarvest,
        };
        string? kbroot = root is null ? null : registry.Resolve(root);
        return Envelope(EventTypes.KnowledgeSearched, pattern, kbroot, data, toolUse, session, repo, task, registry, random);
    }

    private static JsonObject? MapSkillToolUse(
        MinedToolUse toolUse,
        JsonObject raw,
        string session,
        string? repo,
        string? task,
        KnowledgeRegistry registry,
        Random random)
    {
        string? skill = (string?)toolUse.Input[HookPayload.Skill];
        if (skill is null)
        {
            return null;
        }
        JsonObject data = new()
        {
            [EventDataFields.Skill] = skill,
            [EventDataFields.Raw] = raw,
            [EventDataFields.Origin] = EventDataFields.OriginHarvest,
        };
        return Envelope(EventTypes.SkillInvoked, skill, kbroot: null, data, toolUse, session, repo, task, registry, random);
    }

    private static JsonObject? MapWriteToolUse(
        MinedToolUse toolUse,
        JsonObject raw,
        string session,
        string? repo,
        string? task,
        KnowledgeRegistry registry,
        Random random)
    {
        string? filePath = ClaudeCodeAdapter.AbsolutePath(
            (string?)toolUse.Input[HookPayload.FilePath] ?? (string?)toolUse.Input[HookPayload.NotebookPath], toolUse.Cwd);
        if (filePath is null)
        {
            return null;
        }
        if (raw[HookPayload.ToolInput] is JsonObject rawToolInput)
        {
            ClaudeCodeAdapter.StripWrittenContent(rawToolInput);
        }
        JsonObject data = new()
        {
            [EventDataFields.Path] = filePath,
            [EventDataFields.ContentHash] = null,
            [EventDataFields.Linkcount] = null,
            [EventDataFields.Raw] = raw,
            [EventDataFields.Origin] = EventDataFields.OriginHarvest,
        };
        return Envelope(EventTypes.KnowledgeWritten, filePath, registry.Resolve(filePath), data, toolUse, session, repo, task, registry, random, EventTypes.KnowledgeWrittenV2);
    }

    private static JsonObject Envelope(
        string type,
        string subject,
        string? kbroot,
        JsonObject data,
        MinedToolUse toolUse,
        string session,
        string? repo,
        string? task,
        KnowledgeRegistry registry,
        Random random,
        string? schemaRef = null)
    {
        return EventEnvelope.Create(
            type, subject, kbroot, data, registry.Machine, ClaudeCodeAdapter.AgentName,
            session, repo, task, toolUse.Model, toolUse.Time, random, schemaRef);
    }

    private static JsonObject? SumUsage(Dictionary<string, JsonObject> usageByRequest)
    {
        if (usageByRequest.Count is 0)
        {
            return null;
        }

        long inputTokens = 0;
        long cacheReadTokens = 0;
        long outputTokens = 0;
        foreach (JsonObject usage in usageByRequest.Values)
        {
            inputTokens += (long?)usage["input_tokens"] ?? 0;
            cacheReadTokens += (long?)usage["cache_read_input_tokens"] ?? 0;
            outputTokens += (long?)usage["output_tokens"] ?? 0;
        }

        return new JsonObject
        {
            ["input_tokens"] = inputTokens,
            ["cache_read_tokens"] = cacheReadTokens,
            ["output_tokens"] = outputTokens,
        };
    }

    private static int? AuthoritativeHits(JsonNode? toolUseResult)
    {
        if (toolUseResult is not JsonObject result)
        {
            return null;
        }
        if (result["filenames"] is JsonArray filenames)
        {
            return filenames.Count;
        }
        foreach (string key in new[] { "numFiles", "numLines", "numMatches", "count" })
        {
            if (result[key] is JsonValue value && value.TryGetValue(out int hits))
            {
                return hits;
            }
        }
        return null;
    }

    private static DateTimeOffset? ParseTime(string? timestamp)
    {
        if (timestamp is null)
        {
            return null;
        }
        return DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed)
            ? parsed
            : null;
    }
}