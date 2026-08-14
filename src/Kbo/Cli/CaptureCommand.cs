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

        JsonObject payload;
        try
        {
            payload = JsonNode.Parse(input.ReadToEnd()) as JsonObject
                ?? throw new JsonException("hook payload is not a JSON object");
        }
        catch (JsonException exception)
        {
            error.WriteLine($"invalid hook payload: {exception.Message}");
            return 1;
        }

        KnowledgeRegistry registry;
        try
        {
            registry = KnowledgeRegistry.Load(RegistryLocator.Locate(null, environment, homeDirectory));
        }
        catch (RegistryFormatException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
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
                error.WriteLine($"unsupported hook event '{hookEventName}' for agent '{agent}'");
                return 1;
        }

        if (events.Count == 0)
        {
            return 0;
        }

        EventValidator validator = new();
        foreach (JsonObject envelopeEvent in events)
        {
            EventValidationResult result = validator.Validate(envelopeEvent.ToJsonString());
            if (!result.IsValid)
            {
                error.WriteLine($"mapped event failed validation: {string.Join("; ", result.Errors)}");
                return 1;
            }
        }

        string eventsRepo = environment(KboEnvironment.EventsRepoVariable)
            ?? KboEnvironment.DefaultEventsRepo(homeDirectory);
        new BronzeStore(eventsRepo).Append(events);
        return 0;
    }
}
