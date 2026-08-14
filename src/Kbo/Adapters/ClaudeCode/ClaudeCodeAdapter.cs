using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Adapters.ClaudeCode;

public static class ClaudeCodeAdapter
{
    public const string AgentName = "claude-code";
    private const long HashSizeCapBytes = 5 * 1024 * 1024;

    public static JsonObject? MapPostToolUse(JsonObject payload, KnowledgeRegistry registry, TimeProvider clock, Random random)
    {
        string? toolName = (string?)payload[HookPayload.ToolName];
        JsonObject? toolInput = payload[HookPayload.ToolInput] as JsonObject;
        if (toolName is null || toolInput is null)
        {
            return null;
        }

        string? cwd = (string?)payload[HookPayload.Cwd];
        switch (toolName)
        {
            case HookPayload.Tools.Read:
            {
                string? filePath = AbsolutePath((string?)toolInput[HookPayload.FilePath], cwd);
                if (filePath is null)
                {
                    return null;
                }
                string? kbroot = registry.Resolve(filePath);
                JsonObject data = new() { [EventDataFields.Path] = filePath };
                AddContentHash(data, filePath, kbroot);
                data[EventDataFields.Raw] = RawPayload(payload);
                return Envelope(EventTypes.KnowledgeRead, filePath, kbroot, data, payload, registry, clock, random);
            }
            case HookPayload.Tools.Grep:
            case HookPayload.Tools.Glob:
            {
                string? pattern = (string?)toolInput[HookPayload.Pattern];
                if (pattern is null)
                {
                    return null;
                }
                string? root = AbsolutePath((string?)toolInput[HookPayload.Path], cwd) ?? AbsolutePath(cwd, null);
                string? kbroot = root is null ? null : registry.Resolve(root);
                JsonObject data = new()
                {
                    [EventDataFields.Pattern] = pattern,
                    [EventDataFields.Root] = root,
                    [EventDataFields.Hits] = BestEffortHits(payload[HookPayload.ToolResponse]),
                    [EventDataFields.Raw] = RawPayload(payload),
                };
                return Envelope(EventTypes.KnowledgeSearched, pattern, kbroot, data, payload, registry, clock, random);
            }
            case HookPayload.Tools.Write:
            case HookPayload.Tools.Edit:
            case HookPayload.Tools.NotebookEdit:
            {
                string? filePath = AbsolutePath(
                    (string?)toolInput[HookPayload.FilePath] ?? (string?)toolInput[HookPayload.NotebookPath], cwd);
                if (filePath is null)
                {
                    return null;
                }
                string? kbroot = registry.Resolve(filePath);
                JsonObject data = new()
                {
                    [EventDataFields.Path] = filePath,
                    [EventDataFields.Raw] = RawPayload(payload),
                };
                return Envelope(EventTypes.KnowledgeWritten, filePath, kbroot, data, payload, registry, clock, random);
            }
            default:
                return null;
        }
    }

    public static List<JsonObject> MapSessionStart(
        JsonObject payload,
        KnowledgeRegistry registry,
        TimeProvider clock,
        Random random,
        string homeDirectory)
    {
        List<JsonObject> events = new();
        string? cwd = (string?)payload[HookPayload.Cwd];

        GitContext git = GitContext.Discover(cwd);
        JsonObject sessionData = new()
        {
            [EventDataFields.Branch] = git.Branch,
            [EventDataFields.Usage] = null,
            [EventDataFields.Raw] = RawPayload(payload),
        };
        events.Add(Envelope(
            EventTypes.SessionStarted, (string?)payload[HookPayload.SessionId], null, sessionData, payload, registry, clock, random));

        foreach ((string path, string kind) in ImplicitContextFiles(cwd, homeDirectory))
        {
            string? kbroot = registry.Resolve(path);
            JsonObject data = new() { [EventDataFields.Path] = path };
            AddContentHash(data, path, kbroot);
            JsonObject raw = RawPayload(payload);
            raw[EventDataFields.Kind] = kind;
            data[EventDataFields.Raw] = raw;
            events.Add(Envelope(EventTypes.ContextLoaded, path, kbroot, data, payload, registry, clock, random));
        }

        return events;
    }

    private static IEnumerable<(string Path, string Kind)> ImplicitContextFiles(string? cwd, string homeDirectory)
    {
        string globalInstructions = Path.Combine(homeDirectory, ".claude", "CLAUDE.md");
        if (File.Exists(globalInstructions))
        {
            yield return (globalInstructions, HookPayload.ContextKinds.GlobalInstructions);
        }

        if (cwd is null)
        {
            yield break;
        }

        string projectInstructions = Path.Combine(cwd, "CLAUDE.md");
        if (File.Exists(projectInstructions))
        {
            yield return (projectInstructions, HookPayload.ContextKinds.ProjectInstructions);
        }

        string rulesDirectory = Path.Combine(cwd, ".claude", "rules");
        if (Directory.Exists(rulesDirectory))
        {
            foreach (string rulePath in Directory.EnumerateFiles(rulesDirectory, "*.md").Order())
            {
                yield return (rulePath, HookPayload.ContextKinds.Rules);
            }
        }

        string memoryIndex = Path.Combine(
            homeDirectory, ".claude", "projects", cwd.Replace('/', '-'), "memory", "MEMORY.md");
        if (File.Exists(memoryIndex))
        {
            yield return (memoryIndex, HookPayload.ContextKinds.Memory);
        }
    }

    private static int? BestEffortHits(JsonNode? toolResponse)
    {
        if (toolResponse is JsonObject response)
        {
            foreach (string key in new[] { "numFiles", "numLines", "numMatches", "count" })
            {
                if (response[key] is JsonValue value && value.TryGetValue(out int hits))
                {
                    return hits;
                }
            }
            if (response["filenames"] is JsonArray filenames)
            {
                return filenames.Count;
            }
        }
        return null;
    }

    private static void AddContentHash(JsonObject data, string filePath, string? kbroot)
    {
        data[EventDataFields.ContentHash] = null;
        if (kbroot is null || !File.Exists(filePath))
        {
            return;
        }

        long size = new FileInfo(filePath).Length;
        if (size > HashSizeCapBytes)
        {
            data[EventDataFields.Size] = size;
            return;
        }

        using FileStream stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        data[EventDataFields.ContentHash] = Convert.ToHexStringLower(hash)[..16];
    }

    private static JsonObject RawPayload(JsonObject payload)
    {
        JsonObject raw = (JsonObject)payload.DeepClone();
        raw.Remove(HookPayload.ToolResponse);
        return raw;
    }

    internal static string? AbsolutePath(string? path, string? cwd)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }
        return cwd is null ? null : Path.GetFullPath(Path.Combine(cwd, path));
    }

    private static JsonObject Envelope(
        string type,
        string? subject,
        string? kbroot,
        JsonObject data,
        JsonObject payload,
        KnowledgeRegistry registry,
        TimeProvider clock,
        Random random)
    {
        GitContext git = GitContext.Discover((string?)payload[HookPayload.Cwd]);
        data[EventDataFields.Origin] = EventDataFields.OriginHook;

        return EventEnvelope.Create(
            type,
            subject,
            kbroot,
            data,
            registry.Machine,
            AgentName,
            session: (string?)payload[HookPayload.SessionId],
            repo: git.RepoRoot,
            task: git.Task,
            model: null,
            time: clock.GetUtcNow(),
            random: random);
    }
}
