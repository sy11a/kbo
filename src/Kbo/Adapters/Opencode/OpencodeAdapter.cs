using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Kbo.Adapters.ClaudeCode;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Adapters.Opencode;

/// <summary>
/// Maps the kbo opencode plugin's payloads to envelope events. Every opencode
/// event carries data.transcript = session id — the session row is this
/// agent's "transcript file" unit (ADR-0014), so audit and harvest idempotency
/// reuse the existing stamp machinery.
/// </summary>
public static class OpencodeAdapter
{
    private const long HashSizeCapBytes = 5 * 1024 * 1024;

    public static class Payload
    {
        public const string HookEventName = "hook_event_name";
        public const string SessionId = "session_id";
        public const string Directory = "directory";
        public const string Tool = "tool";
        public const string Args = "args";
        public const string FilePath = "filePath";
        public const string Pattern = "pattern";
        public const string Path = "path";

        public const string ToolExecuteAfter = "tool.execute.after";
        public const string SessionStart = "session.start";
    }

    public static class Tools
    {
        public const string Read = "read";
        public const string Grep = "grep";
        public const string Glob = "glob";
        public const string Write = "write";
        public const string Edit = "edit";
    }

    public static JsonObject? MapToolExecute(JsonObject payload, KnowledgeRegistry registry, TimeProvider clock, Random random)
    {
        string? tool = (string?)payload[Payload.Tool];
        JsonObject? args = payload[Payload.Args] as JsonObject;
        if (tool is null || args is null)
        {
            return null;
        }

        string? directory = (string?)payload[Payload.Directory];
        switch (tool)
        {
            case Tools.Read:
            {
                string? filePath = ClaudeCodeAdapter.AbsolutePath((string?)args[Payload.FilePath], directory);
                if (filePath is null)
                {
                    return null;
                }
                string? kbroot = registry.Resolve(filePath);
                JsonObject data = new() { [EventDataFields.Path] = filePath };
                AddContentHash(data, filePath, kbroot);
                data[EventDataFields.Raw] = payload.DeepClone();
                return Envelope(EventTypes.KnowledgeRead, filePath, kbroot, data, payload, registry, clock.GetUtcNow(), random);
            }
            case Tools.Grep:
            case Tools.Glob:
            {
                string? pattern = (string?)args[Payload.Pattern];
                if (pattern is null)
                {
                    return null;
                }
                string? root = ClaudeCodeAdapter.AbsolutePath((string?)args[Payload.Path], directory)
                    ?? ClaudeCodeAdapter.AbsolutePath(directory, null);
                JsonObject data = new()
                {
                    [EventDataFields.Pattern] = pattern,
                    [EventDataFields.Root] = root,
                    [EventDataFields.Hits] = null,
                    [EventDataFields.Raw] = payload.DeepClone(),
                };
                string? kbroot = root is null ? null : registry.Resolve(root);
                return Envelope(EventTypes.KnowledgeSearched, pattern, kbroot, data, payload, registry, clock.GetUtcNow(), random);
            }
            case Tools.Write:
            case Tools.Edit:
            {
                string? filePath = ClaudeCodeAdapter.AbsolutePath((string?)args[Payload.FilePath], directory);
                if (filePath is null)
                {
                    return null;
                }
                JsonObject data = new()
                {
                    [EventDataFields.Path] = filePath,
                    [EventDataFields.Raw] = payload.DeepClone(),
                };
                return Envelope(EventTypes.KnowledgeWritten, filePath, registry.Resolve(filePath), data, payload, registry, clock.GetUtcNow(), random);
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
        string opencodeConfigDirectory)
    {
        List<JsonObject> events = new();
        string? directory = (string?)payload[Payload.Directory];
        GitContext git = GitContext.Discover(directory);

        JsonObject sessionData = new()
        {
            [EventDataFields.Branch] = git.Branch,
            [EventDataFields.Usage] = null,
            [EventDataFields.Raw] = payload.DeepClone(),
        };
        events.Add(Envelope(
            EventTypes.SessionStarted, (string?)payload[Payload.SessionId], null, sessionData, payload, registry, clock.GetUtcNow(), random));

        foreach ((string path, string kind) in ImplicitContextFiles(directory, opencodeConfigDirectory))
        {
            string? kbroot = registry.Resolve(path);
            JsonObject data = new() { [EventDataFields.Path] = path };
            AddContentHash(data, path, kbroot);
            JsonObject raw = (JsonObject)payload.DeepClone();
            raw[EventDataFields.Kind] = kind;
            data[EventDataFields.Raw] = raw;
            events.Add(Envelope(EventTypes.ContextLoaded, path, kbroot, data, payload, registry, clock.GetUtcNow(), random));
        }

        return events;
    }

    private static IEnumerable<(string Path, string Kind)> ImplicitContextFiles(string? directory, string opencodeConfigDirectory)
    {
        string globalAgents = System.IO.Path.Combine(opencodeConfigDirectory, "AGENTS.md");
        if (File.Exists(globalAgents))
        {
            yield return (globalAgents, "global-instructions");
        }
        if (directory is not null)
        {
            string projectAgents = System.IO.Path.Combine(directory, "AGENTS.md");
            if (File.Exists(projectAgents))
            {
                yield return (projectAgents, "project-instructions");
            }
        }
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

    private static JsonObject Envelope(
        string type,
        string? subject,
        string? kbroot,
        JsonObject data,
        JsonObject payload,
        KnowledgeRegistry registry,
        DateTimeOffset time,
        Random random)
    {
        GitContext git = GitContext.Discover((string?)payload[Payload.Directory]);
        string? session = (string?)payload[Payload.SessionId];
        data[EventDataFields.Origin] = EventDataFields.OriginHook;
        if (session is not null)
        {
            data[EventDataFields.Transcript] = session;
        }

        return EventEnvelope.Create(
            type,
            subject,
            kbroot,
            data,
            registry.Machine,
            OpencodeRetention.AgentName,
            session,
            repo: git.RepoRoot,
            task: git.Task,
            model: null,
            time,
            random);
    }
}
