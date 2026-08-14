using System.Text.Json.Nodes;
using Kbo.Adapters.ClaudeCode;
using Kbo.Adapters.Opencode;
using Kbo.Bronze;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Cli;

public static class HarvestCommand
{
    private const string Usage = $"usage: kbo harvest <{ClaudeCodeAdapter.AgentName} [--transcripts <dir>] [--backfill-skills] | {OpencodeRetention.AgentName} [--db <file>]>";

    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string, string?> environment,
        string homeDirectory)
    {
        if (args.Length == 0 || (args[0] != ClaudeCodeAdapter.AgentName && args[0] != OpencodeRetention.AgentName))
        {
            error.WriteLine(Usage);
            return 1;
        }
        string agent = args[0];

        string transcriptsRoot = Path.Combine(homeDirectory, ".claude", "projects");
        string databasePath = Path.Combine(homeDirectory, ".local", "share", "opencode", "opencode.db");
        bool backfillSkills = false;
        for (int index = 1; index < args.Length; index++)
        {
            if (agent == ClaudeCodeAdapter.AgentName && args[index] == "--transcripts" && index + 1 < args.Length)
            {
                transcriptsRoot = args[++index];
            }
            else if (agent == ClaudeCodeAdapter.AgentName && args[index] == "--backfill-skills")
            {
                backfillSkills = true;
            }
            else if (agent == OpencodeRetention.AgentName && args[index] == "--db" && index + 1 < args.Length)
            {
                databasePath = args[++index];
            }
            else
            {
                error.WriteLine(Usage);
                return 1;
            }
        }

        if (agent == ClaudeCodeAdapter.AgentName && !Directory.Exists(transcriptsRoot))
        {
            error.WriteLine($"transcripts directory not found: {transcriptsRoot}");
            return 1;
        }
        if (agent == OpencodeRetention.AgentName && !File.Exists(databasePath))
        {
            error.WriteLine($"opencode database not found: {databasePath}");
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

        string eventsRepo = environment(KboEnvironment.EventsRepoVariable)
            ?? KboEnvironment.DefaultEventsRepo(homeDirectory);
        BronzeStore store = new(eventsRepo);
        IReadOnlySet<string> harvestedTranscripts = backfillSkills
            ? store.TranscriptsWithType(EventTypes.SkillInvoked)
            : store.HarvestedTranscripts();
        EventValidator validator = new();

        int harvestedCount = 0;
        int skippedCount = 0;
        int eventCount = 0;
        int invalidCount = 0;

        void AppendValidated(string sourceLabel, List<JsonObject> events)
        {
            if (events.Count == 0)
            {
                return;
            }
            List<JsonObject> validEvents = new();
            foreach (JsonObject minedEvent in events)
            {
                EventValidationResult result = validator.Validate(minedEvent.ToJsonString());
                if (result.IsValid)
                {
                    validEvents.Add(minedEvent);
                }
                else
                {
                    invalidCount++;
                    error.WriteLine($"{sourceLabel}: invalid event dropped: {string.Join("; ", result.Errors)}");
                }
            }
            store.Append(validEvents);
            harvestedCount++;
            eventCount += validEvents.Count;
        }

        if (agent == ClaudeCodeAdapter.AgentName)
        {
            foreach (string transcriptPath in Directory
                .EnumerateFiles(transcriptsRoot, "*.jsonl", SearchOption.AllDirectories)
                .Order())
            {
                string transcriptId = Path.GetFileNameWithoutExtension(transcriptPath);
                if (harvestedTranscripts.Contains(transcriptId))
                {
                    skippedCount++;
                    continue;
                }
                List<JsonObject> mined = TranscriptMiner.Mine(File.ReadLines(transcriptPath), transcriptId, registry, Random.Shared);
                if (backfillSkills)
                {
                    mined = mined.Where(minedEvent => (string?)minedEvent[EnvelopeFields.Type] == EventTypes.SkillInvoked).ToList();
                    if (mined.Count == 0)
                    {
                        continue;
                    }
                }
                AppendValidated(transcriptPath, mined);
            }
        }
        else
        {
            List<string> pendingSessions = new();
            foreach (string sessionId in OpencodeMiner.EnumerateSessionIds(databasePath))
            {
                if (harvestedTranscripts.Contains(sessionId))
                {
                    skippedCount++;
                }
                else
                {
                    pendingSessions.Add(sessionId);
                }
            }
            foreach (string sessionId in pendingSessions)
            {
                AppendValidated(sessionId, OpencodeMiner.Mine(databasePath, new[] { sessionId }, registry, Random.Shared));
            }
        }

        output.WriteLine(
            $"harvested {harvestedCount} session(s), {eventCount} event(s); skipped {skippedCount} already-harvested; {invalidCount} invalid event(s) dropped");
        return 0;
    }
}
