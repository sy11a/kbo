using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Kbo.Schemas;

namespace Kbo.Bronze;

public sealed class BronzeStore
{
    private const string BronzeDirectory = "bronze";
    private const string MonthFileExtension = ".ndjsonl";
    private const string LockDirectory = ".locks";

    private readonly string repositoryRoot;

    public BronzeStore(string repositoryRoot)
    {
        this.repositoryRoot = repositoryRoot;
    }

    public void Append(IEnumerable<JsonObject> events)
    {
        EnsureRepository();

        foreach (JsonObject envelopeEvent in events)
        {
            string machine = RequiredField(envelopeEvent, EnvelopeFields.Machine);
            string agent = RequiredField(envelopeEvent, EnvelopeFields.Agent);
            string month = RequiredField(envelopeEvent, EnvelopeFields.Time)[..7];

            string directory = Path.Combine(repositoryRoot, BronzeDirectory, machine, agent);
            Directory.CreateDirectory(directory);
            string monthFile = Path.Combine(directory, month + MonthFileExtension);

            string lockDirectory = Path.Combine(repositoryRoot, LockDirectory);
            Directory.CreateDirectory(lockDirectory);
            string lockFile = Path.Combine(lockDirectory, $"{machine}-{agent}-{month}.lock");

            byte[] line = Encoding.UTF8.GetBytes(envelopeEvent.ToJsonString() + "\n");
            // Concurrent appenders serialize on a sidecar lock file: FileStream "append"
            // is a positional write, not O_APPEND, so unserialized concurrent appends
            // overwrite each other. The lock lives outside the bronze tree so scanners
            // and jobs never see or block on it (ADR-0030).
            RetryTransientIO(() =>
            {
                using FileStream appendLock = new(lockFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                using FileStream stream = new(monthFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(line);
            });
        }
    }

    // No cross-process signal exists to wait on for the lock file, hence bounded
    // sleep-backoff; exhaustion surfaces as IOException and the capture fail-safe
    // records the drop (ADR-0029, ADR-0030).
    internal static void RetryTransientIO(Action appendAction)
    {
        const int maxAttempts = 10;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                appendAction();
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                // Jittered backoff: contending appenders sleeping a fixed interval
                // would wake and collide in lockstep until the budget is exhausted.
                Thread.Sleep(Random.Shared.Next(5, 20 * attempt));
            }
        }
    }

    public IReadOnlySet<string> HarvestedTranscripts()
    {
        HashSet<string> transcripts = new();
        string bronzeRoot = Path.Combine(repositoryRoot, BronzeDirectory);
        if (!Directory.Exists(bronzeRoot))
        {
            return transcripts;
        }

        foreach (string monthFile in Directory.EnumerateFiles(bronzeRoot, "*" + MonthFileExtension, SearchOption.AllDirectories))
        {
            foreach (string line in File.ReadLines(monthFile))
            {
                JsonObject? envelopeEvent;
                try
                {
                    envelopeEvent = JsonNode.Parse(line) as JsonObject;
                }
                catch (System.Text.Json.JsonException)
                {
                    continue;
                }

                JsonNode? data = envelopeEvent?[EnvelopeFields.Data];
                if (data is not null
                    && (string?)data[EventDataFields.Origin] == EventDataFields.OriginHarvest
                    && (string?)data[EventDataFields.Transcript] is string transcript)
                {
                    transcripts.Add(transcript);
                }
            }
        }

        return transcripts;
    }

    public IReadOnlySet<string> TranscriptsWithType(string eventType)
    {
        HashSet<string> transcripts = new();
        string bronzeRoot = Path.Combine(repositoryRoot, BronzeDirectory);
        if (!Directory.Exists(bronzeRoot))
        {
            return transcripts;
        }

        foreach (string monthFile in Directory.EnumerateFiles(bronzeRoot, "*" + MonthFileExtension, SearchOption.AllDirectories))
        {
            foreach (string line in File.ReadLines(monthFile))
            {
                JsonObject? envelopeEvent;
                try
                {
                    envelopeEvent = JsonNode.Parse(line) as JsonObject;
                }
                catch (System.Text.Json.JsonException)
                {
                    continue;
                }

                if ((string?)envelopeEvent?[EnvelopeFields.Type] == eventType
                    && (string?)envelopeEvent?[EnvelopeFields.Data]?[EventDataFields.Transcript] is string transcript)
                {
                    transcripts.Add(transcript);
                }
            }
        }

        return transcripts;
    }

    public IReadOnlySet<string> SeenTranscripts()
    {
        HashSet<string> transcripts = new();
        string bronzeRoot = Path.Combine(repositoryRoot, BronzeDirectory);
        if (!Directory.Exists(bronzeRoot))
        {
            return transcripts;
        }

        foreach (string monthFile in Directory.EnumerateFiles(bronzeRoot, "*" + MonthFileExtension, SearchOption.AllDirectories))
        {
            foreach (string line in File.ReadLines(monthFile))
            {
                JsonObject? envelopeEvent;
                try
                {
                    envelopeEvent = JsonNode.Parse(line) as JsonObject;
                }
                catch (System.Text.Json.JsonException)
                {
                    continue;
                }

                JsonNode? data = envelopeEvent?[EnvelopeFields.Data];
                if (data is null)
                {
                    continue;
                }
                if ((string?)data[EventDataFields.Transcript] is string stamped)
                {
                    transcripts.Add(stamped);
                }
                else if ((string?)data[EventDataFields.Raw]?["transcript_path"] is string transcriptPath)
                {
                    transcripts.Add(Path.GetFileNameWithoutExtension(transcriptPath));
                }
            }
        }

        return transcripts;
    }

    public Dictionary<string, DateTimeOffset> LastCompletedJobs()
    {
        Dictionary<string, DateTimeOffset> lastCompleted = new();
        string bronzeRoot = Path.Combine(repositoryRoot, BronzeDirectory);
        if (!Directory.Exists(bronzeRoot))
        {
            return lastCompleted;
        }

        foreach (string monthFile in Directory.EnumerateFiles(bronzeRoot, "*" + MonthFileExtension, SearchOption.AllDirectories))
        {
            foreach (string line in File.ReadLines(monthFile))
            {
                JsonObject? envelopeEvent;
                try
                {
                    envelopeEvent = JsonNode.Parse(line) as JsonObject;
                }
                catch (System.Text.Json.JsonException)
                {
                    continue;
                }

                if (envelopeEvent is null
                    || (string?)envelopeEvent[EnvelopeFields.Type] != EventTypes.JobCompleted
                    || (string?)envelopeEvent[EnvelopeFields.Subject] is not string job
                    || !DateTimeOffset.TryParse(
                        (string?)envelopeEvent[EnvelopeFields.Time],
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out DateTimeOffset time))
                {
                    continue;
                }

                if (!lastCompleted.TryGetValue(job, out DateTimeOffset existing) || time > existing)
                {
                    lastCompleted[job] = time;
                }
            }
        }

        return lastCompleted;
    }

    private static string RequiredField(JsonObject envelopeEvent, string field)
    {
        return (string?)envelopeEvent[field]
            ?? throw new InvalidOperationException($"event has no '{field}' field");
    }

    private void EnsureRepository()
    {
        EnsureLockFilesIgnored();
        if (Directory.Exists(Path.Combine(repositoryRoot, ".git")))
        {
            return;
        }

        Directory.CreateDirectory(repositoryRoot);
        ProcessStartInfo startInfo = new("git", "init --quiet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start 'git init'");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"'git init' failed in {repositoryRoot}: {process.StandardError.ReadToEnd()}");
        }
    }

    private void EnsureLockFilesIgnored()
    {
        string gitignore = Path.Combine(repositoryRoot, ".gitignore");
        if (File.Exists(gitignore) && File.ReadLines(gitignore).Contains("*.lock"))
        {
            return;
        }

        Directory.CreateDirectory(repositoryRoot);
        File.AppendAllText(gitignore, "*.lock\n");
    }
}
