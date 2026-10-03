using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Adapters.ClaudeCode;
using Kbo.Adapters.Opencode;
using Kbo.Bronze;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Cli;

internal static class HarvestCommand
{
    private const string Usage = $"usage: kbo harvest <{ClaudeCodeAdapter.AgentName} [--transcripts <dir>] | {OpencodeRetention.AgentName} [--db <file>]> [--backfill-skills]";

    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string, string?> environment,
        string homeDirectory)
    {
        HarvestArguments? parsed = ParseArguments(args, error, homeDirectory);
        if (parsed is null)
        {
            return 1;
        }
        if (!VerifySource(parsed, error))
        {
            return 1;
        }
        KnowledgeRegistry registry;
        try
        {
            registry = LoadRegistry(environment, homeDirectory);
        }
        catch (RegistryFormatException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
        HarvestState state = CreateState(parsed, environment, homeDirectory, error);
        if (parsed.Agent is ClaudeCodeAdapter.AgentName)
        {
            HarvestClaudeCode(parsed, registry, state);
        }
        else
        {
            HarvestOpencode(parsed, registry, state);
        }
        WriteSummary(state, output);
        return 0;
    }

    private sealed record HarvestArguments(string Agent, string TranscriptsRoot, string DatabasePath, bool BackfillSkills);

    private sealed class HarvestState
    {
        public readonly BronzeStore Store;
        public readonly IReadOnlySet<string> HarvestedTranscripts;
        public readonly EventValidator Validator;
        public readonly TextWriter Error;
        public int HarvestedCount;
        public int SkippedCount;
        public int EventCount;
        public int InvalidCount;

        public HarvestState(BronzeStore store, IReadOnlySet<string> harvestedTranscripts, EventValidator validator, TextWriter error)
        {
            Store = store;
            HarvestedTranscripts = harvestedTranscripts;
            Validator = validator;
            Error = error;
        }
    }

    private static HarvestArguments? ParseArguments(string[] args, TextWriter error, string homeDirectory)
    {
        if (args.Length is 0 || (args[0] is not ClaudeCodeAdapter.AgentName && args[0] is not OpencodeRetention.AgentName))
        {
            error.WriteLine(Usage);
            return null;
        }
        string agent = args[0];

        string transcriptsRoot = Path.Combine(homeDirectory, ".claude", "projects");
        string databasePath = Path.Combine(homeDirectory, ".local", "share", "opencode", "opencode.db");
        bool backfillSkills = false;
        for (int index = 1; index < args.Length; index++)
        {
            if (agent is ClaudeCodeAdapter.AgentName && args[index] is "--transcripts" && index + 1 < args.Length)
            {
                transcriptsRoot = args[++index];
            }
            else if (args[index] is "--backfill-skills")
            {
                backfillSkills = true;
            }
            else if (agent is OpencodeRetention.AgentName && args[index] is "--db" && index + 1 < args.Length)
            {
                databasePath = args[++index];
            }
            else
            {
                error.WriteLine(Usage);
                return null;
            }
        }
        return new HarvestArguments(agent, transcriptsRoot, databasePath, backfillSkills);
    }

    private static bool VerifySource(HarvestArguments parsed, TextWriter error)
    {
        switch (parsed.Agent)
        {
            case ClaudeCodeAdapter.AgentName when !Directory.Exists(parsed.TranscriptsRoot):
                {
                    error.WriteLine($"transcripts directory not found: {parsed.TranscriptsRoot}");
                    return false;
                }
            case OpencodeRetention.AgentName when !File.Exists(parsed.DatabasePath):
                {
                    error.WriteLine($"opencode database not found: {parsed.DatabasePath}");
                    return false;
                }
        }
        return true;
    }

    private static KnowledgeRegistry LoadRegistry(Func<string, string?> environment, string homeDirectory)
    {
        return KnowledgeRegistry.Load(
            RegistryLocator.Locate(explicitPath: null, environment, homeDirectory),
            environment(KboEnvironment.TaskPatternVariable));
    }

    private static HarvestState CreateState(HarvestArguments parsed, Func<string, string?> environment, string homeDirectory, TextWriter error)
    {
        string eventsRepo = environment(KboEnvironment.EventsRepoVariable)
            ?? KboEnvironment.DefaultEventsRepo(homeDirectory);
        BronzeStore store = new(eventsRepo);
        IReadOnlySet<string> harvestedTranscripts = parsed.BackfillSkills
            ? store.TranscriptsWithType(EventTypes.SkillInvoked)
            : store.HarvestedTranscripts();
        EventValidator validator = new();
        return new HarvestState(store, harvestedTranscripts, validator, error);
    }

    private static void HarvestClaudeCode(HarvestArguments parsed, KnowledgeRegistry registry, HarvestState state)
    {
        foreach (string transcriptPath in Directory
            .EnumerateFiles(parsed.TranscriptsRoot, "*.jsonl", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal))
        {
            string transcriptId = Path.GetFileNameWithoutExtension(transcriptPath);
            if (state.HarvestedTranscripts.Contains(transcriptId))
            {
                state.SkippedCount++;
                continue;
            }
            List<JsonObject> mined = TranscriptMiner.Mine(File.ReadLines(transcriptPath), transcriptId, registry, Random.Shared);
            AppendValidated(transcriptPath, FilterForBackfill(mined, parsed.BackfillSkills), state);
        }
    }

    private static void HarvestOpencode(HarvestArguments parsed, KnowledgeRegistry registry, HarvestState state)
    {
        List<string> pendingSessions = [];
        foreach (string sessionId in OpencodeMiner.EnumerateSessionIds(parsed.DatabasePath))
        {
            if (state.HarvestedTranscripts.Contains(sessionId))
            {
                state.SkippedCount++;
            }
            else
            {
                pendingSessions.Add(sessionId);
            }
        }
        foreach (string sessionId in pendingSessions)
        {
            AppendValidated(sessionId, FilterForBackfill(OpencodeMiner.Mine(parsed.DatabasePath, new[] { sessionId }, registry, Random.Shared), parsed.BackfillSkills), state);
        }
    }

    private static void AppendValidated(string sourceLabel, List<JsonObject> events, HarvestState state)
    {
        if (events.Count is 0)
        {
            return;
        }
        List<JsonObject> validEvents = [];
        foreach (JsonObject minedEvent in events)
        {
            EventValidationResult result = state.Validator.Validate(minedEvent.ToJsonString());
            if (result.IsValid)
            {
                validEvents.Add(minedEvent);
            }
            else
            {
                state.InvalidCount++;
                state.Error.WriteLine($"{sourceLabel}: invalid event dropped: {string.Join("; ", result.Errors)}");
            }
        }
        state.Store.Append(validEvents);
        state.HarvestedCount++;
        state.EventCount += validEvents.Count;
    }

    private static List<JsonObject> FilterForBackfill(List<JsonObject> mined, bool backfillSkills)
    {
        return backfillSkills
            ? [.. mined.Where(minedEvent => (string?)minedEvent[EnvelopeFields.Type] == EventTypes.SkillInvoked)]
            : mined;
    }

    private static void WriteSummary(HarvestState state, TextWriter output)
    {
        output.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"harvested {state.HarvestedCount} session(s), {state.EventCount} event(s); skipped {state.SkippedCount} already-harvested; {state.InvalidCount} invalid event(s) dropped"));
    }
}
