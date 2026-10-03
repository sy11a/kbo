using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Nodes;
using Kbo.Schemas;

namespace Kbo.Bronze;

internal sealed class BronzeStore
{
    private const string BronzeDirectory = "bronze";
    private const string MonthFileExtension = ".ndjsonl";
    private const string LockDirectory = ".locks";

    private readonly string _repositoryRoot;

    public BronzeStore(string repositoryRoot) => this._repositoryRoot = repositoryRoot;

    public void Append(IEnumerable<JsonObject> events)
    {
        EnsureRepository();

        foreach (JsonObject envelopeEvent in events)
        {
            string machine = RequiredField(envelopeEvent, EnvelopeFields.Machine);
            string agent = RequiredField(envelopeEvent, EnvelopeFields.Agent);
            string month = RequiredField(envelopeEvent, EnvelopeFields.Time)[..7];

            string directory = Path.Combine(_repositoryRoot, BronzeDirectory, machine, agent);
            _ = Directory.CreateDirectory(directory);
            string monthFile = Path.Combine(directory, month + MonthFileExtension);

            string lockDirectory = Path.Combine(_repositoryRoot, LockDirectory);
            _ = Directory.CreateDirectory(lockDirectory);
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
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Backoff jitter is contention-spreading, not a security primitive.")]
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
        HashSet<string> transcripts = [];
        foreach (JsonObject envelopeEvent in ReadEvents())
        {
            JsonNode? data = envelopeEvent[EnvelopeFields.Data];
            if (data is not null
                && (string?)data[EventDataFields.Origin] == EventDataFields.OriginHarvest
                && (string?)data[EventDataFields.Transcript] is string transcript)
            {
                _ = transcripts.Add(transcript);
            }
        }

        return transcripts;
    }

    public IReadOnlySet<string> TranscriptsWithType(string eventType)
    {
        HashSet<string> transcripts = [];
        foreach (JsonObject envelopeEvent in ReadEvents())
        {
            if ((string?)envelopeEvent[EnvelopeFields.Type] == eventType
                && (string?)envelopeEvent[EnvelopeFields.Data]?[EventDataFields.Transcript] is string transcript)
            {
                _ = transcripts.Add(transcript);
            }
        }

        return transcripts;
    }

    public IReadOnlySet<string> SeenTranscripts()
    {
        HashSet<string> transcripts = [];
        foreach (JsonObject envelopeEvent in ReadEvents())
        {
            JsonNode? data = envelopeEvent[EnvelopeFields.Data];
            if (data is null)
            {
                continue;
            }
            if ((string?)data[EventDataFields.Transcript] is string stamped)
            {
                _ = transcripts.Add(stamped);
            }
            else if ((string?)data[EventDataFields.Raw]?["transcript_path"] is string transcriptPath)
            {
                _ = transcripts.Add(Path.GetFileNameWithoutExtension(transcriptPath));
            }
        }

        return transcripts;
    }

    public Dictionary<string, DateTimeOffset> LastCompletedJobs()
    {
        Dictionary<string, DateTimeOffset> lastCompleted = [];
        foreach (JsonObject envelopeEvent in ReadEvents())
        {
            if ((string?)envelopeEvent[EnvelopeFields.Type] != EventTypes.JobCompleted
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

        return lastCompleted;
    }

    /// <summary>
    /// Dedup keys ("date|source") of every graph.metrics event already in
    /// bronze — the idempotency fence the ingest job checks before appending
    /// (kbl ADR-0006 invariant 4: one bronze line per snapshot, ever).
    /// </summary>
    public IReadOnlySet<string> GraphMetricsKeys()
    {
        HashSet<string> keys = [];
        foreach (JsonObject envelopeEvent in ReadEvents())
        {
            if ((string?)envelopeEvent[EnvelopeFields.Type] != EventTypes.GraphMetrics)
            {
                continue;
            }

            JsonNode? data = envelopeEvent[EnvelopeFields.Data];
            if ((string?)data?[EventDataFields.Date] is string date
                && (string?)data?[EventDataFields.Source] is string source)
            {
                _ = keys.Add(date + "|" + source);
            }
        }

        return keys;
    }

    // Every bronze scan is this loop: enumerate month files, parse each line,
    // skip lines that aren't a JSON object — a crashed writer can leave a
    // truncated tail line, and one bad line must not poison a whole scan.
    private IEnumerable<JsonObject> ReadEvents()
    {
        string bronzeRoot = Path.Combine(_repositoryRoot, BronzeDirectory);
        if (!Directory.Exists(bronzeRoot))
        {
            yield break;
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

                if (envelopeEvent is not null)
                {
                    yield return envelopeEvent;
                }
            }
        }
    }

    private static string RequiredField(JsonObject envelopeEvent, string field)
    {
        return (string?)envelopeEvent[field]
            ?? throw new InvalidOperationException($"event has no '{field}' field");
    }

    private void EnsureRepository()
    {
        EnsureLockFilesIgnored();
        if (Directory.Exists(Path.Combine(_repositoryRoot, ".git")))
        {
            return;
        }

        _ = Directory.CreateDirectory(_repositoryRoot);
        ProcessStartInfo startInfo = new("git", "init --quiet")
        {
            WorkingDirectory = _repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start 'git init'");
        process.WaitForExit();
        if (process.ExitCode is 0)
        {
            return;
        }
        throw new InvalidOperationException($"'git init' failed in {_repositoryRoot}: {process.StandardError.ReadToEnd()}");
    }

    private void EnsureLockFilesIgnored()
    {
        string gitignore = Path.Combine(_repositoryRoot, ".gitignore");
        if (File.Exists(gitignore) && File.ReadLines(gitignore).Contains("*.lock", StringComparer.Ordinal))
        {
            return;
        }

        _ = Directory.CreateDirectory(_repositoryRoot);
        File.AppendAllText(gitignore, "*.lock\n");
    }
}
