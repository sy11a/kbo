using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kbo.Adapters.ClaudeCode;
using Kbo.Adapters.Opencode;
using Kbo.Bronze;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Cli;

internal static class CaptureCommand
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
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or RegistryFormatException
            or InvalidOperationException
            or FormatException
            or ArgumentException
            or RegexMatchTimeoutException)
        {
            LogDrop(homeDirectory, agent, exception.Message);
        }
        return 0;
    }

    /// <summary>
    /// Last-chance handler for any exception type not covered by <see cref="Run"/>'s
    /// filter. Registered against <see cref="AppDomain.CurrentDomain"/> in
    /// <see cref="Program.RunCapture"/> so even a throw from an unfiltered code
    /// path (or the BCL itself) still ends in a logged drop and exit 0
    /// (ADR-0029). The handler logs the drop and ends the process with
    /// exit code 0, so an exception type outside <see cref="Run"/>'s filter still
    /// honours ADR-0029.
    /// </summary>
    internal static UnhandledExceptionEventHandler CreateUnhandledExceptionHandler(string homeDirectory, string agent)
    {
        return (_, eventArgs) =>
        {
            string? message = (eventArgs.ExceptionObject as Exception)?.Message ?? eventArgs.ExceptionObject.GetType().FullName;
            LogDrop(homeDirectory, agent, message ?? string.Empty);
            Environment.Exit(0);
        };
    }

    private static void Capture(string agent, TextReader input, Func<string, string?> environment, string homeDirectory)
    {
        JsonObject? payload = TryParsePayload(input);
        if (payload is null)
        {
            LogDrop(homeDirectory, agent, "invalid hook payload (not a JSON object)");
            return;
        }

        KnowledgeRegistry? registry = TryLoadRegistry(environment, homeDirectory, out string? errorMessage);
        if (registry is null)
        {
            LogDrop(homeDirectory, agent, $"registry: {errorMessage}");
            return;
        }

        List<JsonObject> events = MapEvents(agent, payload, registry, homeDirectory);
        // An unsupported hook event for a known agent is a benign no-op,
        // like an untracked tool — nothing to capture, nothing to log.
        if (events.Count is 0)
        {
            return;
        }

        List<JsonObject> validEvents = ValidateEvents(agent, events, homeDirectory);
        if (validEvents.Count is 0)
        {
            return;
        }

        AppendEvents(validEvents, environment, homeDirectory);
    }

    private static JsonObject? TryParsePayload(TextReader input)
    {
        try
        {
            return JsonNode.Parse(input.ReadToEnd()) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static KnowledgeRegistry? TryLoadRegistry(
        Func<string, string?> environment,
        string homeDirectory,
        out string? errorMessage)
    {
        try
        {
            errorMessage = null;
            return KnowledgeRegistry.Load(
                RegistryLocator.Locate(explicitPath: null, environment, homeDirectory),
                environment(KboEnvironment.TaskPatternVariable));
        }
        catch (RegistryFormatException exception)
        {
            errorMessage = exception.Message;
            return null;
        }
    }

    private static List<JsonObject> MapEvents(
        string agent,
        JsonObject payload,
        KnowledgeRegistry registry,
        string homeDirectory)
    {
        List<JsonObject> events = [];
        string? hookEventName = (string?)payload[HookPayload.HookEventName];
        switch (agent, hookEventName)
        {
            case (ClaudeCodeAdapter.AgentName, HookPayload.Events.PostToolUse):
                {
                    JsonObject? mapped = ClaudeCodeAdapter.MapPostToolUse(payload, registry, TimeProvider.System, CryptographicUlidEntropy.Instance);
                    if (mapped is not null)
                    {
                        events.Add(mapped);
                    }
                    break;
                }
            case (ClaudeCodeAdapter.AgentName, HookPayload.Events.SessionStart):
                {
                    events.AddRange(ClaudeCodeAdapter.MapSessionStart(payload, registry, TimeProvider.System, CryptographicUlidEntropy.Instance, homeDirectory));
                    break;
                }
            case (OpencodeRetention.AgentName, OpencodeAdapter.Payload.ToolExecuteAfter):
                {
                    JsonObject? opencodeMapped = OpencodeAdapter.MapToolExecute(payload, registry, TimeProvider.System, CryptographicUlidEntropy.Instance);
                    if (opencodeMapped is not null)
                    {
                        events.Add(opencodeMapped);
                    }
                    break;
                }
            case (OpencodeRetention.AgentName, OpencodeAdapter.Payload.SessionStart):
                {
                    events.AddRange(OpencodeAdapter.MapSessionStart(
                        payload, registry, TimeProvider.System, CryptographicUlidEntropy.Instance,
                        Path.Combine(homeDirectory, ".config", "opencode")));
                    break;
                }
        }
        return events;
    }

    private static List<JsonObject> ValidateEvents(string agent, List<JsonObject> events, string homeDirectory)
    {
        // Append the valid events and log any that fail validation, rather than
        // dropping a whole SessionStart batch for one bad member (mirrors harvest).
        EventValidator validator = new();
        List<JsonObject> validEvents = [];
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
        return validEvents;
    }

    private static void AppendEvents(
        List<JsonObject> validEvents,
        Func<string, string?> environment,
        string homeDirectory)
    {
        string eventsRepo = environment(KboEnvironment.EventsRepoVariable)
            ?? KboEnvironment.DefaultEventsRepo(homeDirectory);
        new BronzeStore(eventsRepo).Append(validEvents);
    }

    private static void LogDrop(string homeDirectory, string agent, string reason)
    {
        try
        {
            string logPath = KboEnvironment.CaptureErrorLog(homeDirectory);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd'T'HH:mm:ss'Z'}\t{1}\t{2}\n",
                DateTimeOffset.UtcNow,
                agent,
                reason.Replace('\n', ' ').Replace('\t', ' '));
            File.AppendAllText(logPath, line);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The error log is itself best-effort — never let recording a drop
            // become the thing that disrupts the session.
        }
    }
}
