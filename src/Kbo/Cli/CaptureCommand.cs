using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kbo.Adapters.ClaudeCode;
using Kbo.Adapters.Opencode;
using Kbo.Bronze;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Cli;

public static class CaptureCommand
{
    private const string Usage = $"usage: kbo capture <{ClaudeCodeAdapter.AgentName} | {OpencodeRetention.AgentName}>  (hook JSON on stdin)";

    /// <summary>
    /// Live-capture entry point (hook/plugin). Fail-safe by contract (ADR-0029):
    /// a runtime failure (bad payload, missing/invalid registry, an event that
    /// fails validation, an append error) is recorded to the capture-error log
    /// and returns 0 — observation must never perturb the observed session.
    /// Only genuine CLI misuse (unknown agent/args) returns non-zero.
    /// </summary>
    public static int Run(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        Func<string, string?> environment,
        string homeDirectory)
    {
        if (args is not ([ClaudeCodeAdapter.AgentName] or [OpencodeRetention.AgentName]))
        {
            error.WriteLine(Usage);
            return 1;
        }
        string agent = args[0];

        try
        {
            Capture(agent, input, environment, homeDirectory);
        }
        catch (Exception exception)
        {
            LogDrop(homeDirectory, agent, exception.Message);
        }
        return 0;
    }

    private static void Capture(string agent, TextReader input, Func<string, string?> environment, string homeDirectory)
    {
        JsonObject? payload;
        try
        {
            payload = JsonNode.Parse(input.ReadToEnd()) as JsonObject;
        }
        catch (JsonException)
        {
            payload = null;
        }
        if (payload is null)
        {
            LogDrop(homeDirectory, agent, "invalid hook payload (not a JSON object)");
            return;
        }

        KnowledgeRegistry registry;
        try
        {
            registry = KnowledgeRegistry.Load(
                RegistryLocator.Locate(null, environment, homeDirectory),
                environment(KboEnvironment.TaskPatternVariable));
        }
        catch (RegistryFormatException exception)
        {
            LogDrop(homeDirectory, agent, $"registry: {exception.Message}");
            return;
        }

        List<JsonObject> events = new();
        string? hookEventName = (string?)payload[HookPayload.HookEventName];
        switch (agent, hookEventName)
        {
            case (ClaudeCodeAdapter.AgentName, HookPayload.Events.PostToolUse):
                JsonObject? mapped = ClaudeCodeAdapter.MapPostToolUse(payload, registry, TimeProvider.System, Random.Shared);
                if (mapped is not null)
                {
                    events.Add(mapped);
                }
                break;
            case (ClaudeCodeAdapter.AgentName, HookPayload.Events.SessionStart):
                events.AddRange(ClaudeCodeAdapter.MapSessionStart(payload, registry, TimeProvider.System, Random.Shared, homeDirectory));
                break;
            case (OpencodeRetention.AgentName, OpencodeAdapter.Payload.ToolExecuteAfter):
                JsonObject? opencodeMapped = OpencodeAdapter.MapToolExecute(payload, registry, TimeProvider.System, Random.Shared);
                if (opencodeMapped is not null)
                {
                    events.Add(opencodeMapped);
                }
                break;
            case (OpencodeRetention.AgentName, OpencodeAdapter.Payload.SessionStart):
                events.AddRange(OpencodeAdapter.MapSessionStart(
                    payload, registry, TimeProvider.System, Random.Shared,
                    Path.Combine(homeDirectory, ".config", "opencode")));
                break;
            default:
                // An unsupported hook event for a known agent is a benign no-op,
                // like an untracked tool — nothing to capture, nothing to log.
                return;
        }

        if (events.Count == 0)
        {
            return;
        }

        // Append the valid events and log any that fail validation, rather than
        // dropping a whole SessionStart batch for one bad member (mirrors harvest).
        EventValidator validator = new();
        List<JsonObject> validEvents = new();
        foreach (JsonObject envelopeEvent in events)
        {
            EventValidationResult result = validator.Validate(envelopeEvent.ToJsonString());
            if (result.IsValid)
            {
                validEvents.Add(envelopeEvent);
            }
            else
            {
                LogDrop(homeDirectory, agent, $"event failed validation: {string.Join("; ", result.Errors)}");
            }
        }

        if (validEvents.Count == 0)
        {
            return;
        }

        string eventsRepo = environment(KboEnvironment.EventsRepoVariable)
            ?? KboEnvironment.DefaultEventsRepo(homeDirectory);
        new BronzeStore(eventsRepo).Append(validEvents);
    }

    private static void LogDrop(string homeDirectory, string agent, string reason)
    {
        try
        {
            string logPath = KboEnvironment.CaptureErrorLog(homeDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd'T'HH:mm:ss'Z'}\t{1}\t{2}\n",
                DateTimeOffset.UtcNow,
                agent,
                reason.Replace('\n', ' ').Replace('\t', ' '));
            File.AppendAllText(logPath, line);
        }
        catch
        {
            // The error log is itself best-effort — never let recording a drop
            // become the thing that disrupts the session.
        }
    }
}
