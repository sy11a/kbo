using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Adapters.ClaudeCode;

public static class TranscriptMiner
{
    private sealed record MinedToolUse(string ToolName, JsonObject Input, string? ToolUseId, DateTimeOffset Time, string? Model, string? Cwd);

    public static List<JsonObject> Mine(
        IEnumerable<string> transcriptLines,
        string transcriptId,
        KnowledgeRegistry registry,
        Random random)
    {
        string? sessionId = null;
        DateTimeOffset? sessionTime = null;
        string? cwd = null;
        string? branch = null;
        string? firstModel = null;
        Dictionary<string, JsonObject> usageByRequest = new();
        Dictionary<string, JsonNode> resultsByToolUseId = new();
        List<MinedToolUse> toolUses = new();

        foreach (string line in transcriptLines)
        {
            JsonObject? record;
            try
            {
                record = JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException)
            {
                continue;
            }
            if (record is null)
            {
                continue;
            }

            sessionId ??= (string?)record["sessionId"];
            cwd ??= (string?)record["cwd"];
            branch ??= (string?)record["gitBranch"];
            DateTimeOffset? recordTime = ParseTime((string?)record["timestamp"]);
            sessionTime ??= recordTime;

            if (record["toolUseResult"] is JsonNode toolUseResult && record["message"]?["content"] is JsonArray resultContent)
            {
                foreach (JsonNode? contentBlock in resultContent)
                {
                    if (contentBlock is JsonObject result
                        && (string?)result["type"] == "tool_result"
                        && (string?)result["tool_use_id"] is string toolUseId)
                    {
                        resultsByToolUseId[toolUseId] = toolUseResult;
                    }
                }
            }

            if ((string?)record["type"] != "assistant" || record["message"] is not JsonObject message)
            {
                continue;
            }

            string? model = (string?)message["model"];
            firstModel ??= model;

            if (message["usage"] is JsonObject usage && (string?)record["requestId"] is string requestId)
            {
                usageByRequest[requestId] = usage;
            }

            if (message["content"] is not JsonArray content)
            {
                continue;
            }
            foreach (JsonNode? contentBlock in content)
            {
                if (contentBlock is JsonObject toolUse
                    && (string?)toolUse["type"] == "tool_use"
                    && (string?)toolUse["name"] is string toolName
                    && toolUse["input"] is JsonObject input
                    && recordTime is not null)
                {
                    toolUses.Add(new MinedToolUse(
                        toolName, input, (string?)toolUse["id"], recordTime.Value, model, (string?)record["cwd"] ?? cwd));
                }
            }
        }

        if (sessionId is null && sessionTime is null && toolUses.Count == 0)
        {
            return new List<JsonObject>();
        }

        string session = sessionId ?? transcriptId;
        string? repo = GitContext.Discover(cwd, registry.TaskPattern).RepoRoot ?? cwd;
        string? task = GitContext.TaskFromBranch(branch, registry.TaskPattern);

        List<JsonObject> events = new()
        {
            SessionStartedEvent(session, sessionTime, cwd, branch, firstModel, usageByRequest, repo, task, registry, random),
        };

        foreach (MinedToolUse toolUse in toolUses)
        {
            JsonObject? mapped = MapToolUse(toolUse, session, repo, task, registry, resultsByToolUseId, random);
            if (mapped is not null)
            {
                events.Add(mapped);
            }
        }

        foreach (JsonObject minedEvent in events)
        {
            minedEvent[EnvelopeFields.Data]![EventDataFields.Transcript] = transcriptId;
        }

        return events;
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

        switch (toolUse.ToolName)
        {
            case HookPayload.Tools.Read:
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
            case HookPayload.Tools.Grep:
            case HookPayload.Tools.Glob:
            {
                string? pattern = (string?)toolUse.Input[HookPayload.Pattern];
                if (pattern is null)
                {
                    return null;
                }
                string? root = ClaudeCodeAdapter.AbsolutePath((string?)toolUse.Input[HookPayload.Path], toolUse.Cwd)
                    ?? ClaudeCodeAdapter.AbsolutePath(toolUse.Cwd, null);
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
            case HookPayload.Tools.Skill:
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
                return Envelope(EventTypes.SkillInvoked, skill, null, data, toolUse, session, repo, task, registry, random);
            }
            case HookPayload.Tools.Write:
            case HookPayload.Tools.Edit:
            case HookPayload.Tools.NotebookEdit:
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
                    [EventDataFields.Raw] = raw,
                    [EventDataFields.Origin] = EventDataFields.OriginHarvest,
                };
                return Envelope(EventTypes.KnowledgeWritten, filePath, registry.Resolve(filePath), data, toolUse, session, repo, task, registry, random);
            }
            default:
                return null;
        }
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
        Random random)
    {
        return EventEnvelope.Create(
            type, subject, kbroot, data, registry.Machine, ClaudeCodeAdapter.AgentName,
            session, repo, task, toolUse.Model, toolUse.Time, random);
    }

    private static JsonObject? SumUsage(Dictionary<string, JsonObject> usageByRequest)
    {
        if (usageByRequest.Count == 0)
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
