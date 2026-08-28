using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Registry;
using Kbo.Schemas;

namespace Kbo.Jobs;

/// <summary>
/// Daily pull of sibling-repo corpus aggregates (kbl ADR-0006 touchpoint 4,
/// BL-037): reads the export artifact each metricsArtifact registry pointer
/// names — NDJSON of graph.metrics/1 data payloads — and appends them to
/// bronze through the internal path. kbo builds the envelopes; kbl never
/// authors them. Idempotent by dedup key date+source: a snapshot already in
/// bronze is skipped, never re-appended. Absent artifacts are a quiet skip
/// (the consumer ships before the first emit); a present-but-invalid
/// artifact fails the run before anything is appended.
/// </summary>
public sealed class IngestGraphMetricsJob : IPulseJob
{
    private readonly KnowledgeRegistry registry;
    private readonly string eventsRepo;
    private readonly TimeProvider clock;
    private readonly Random random;

    public IngestGraphMetricsJob(KnowledgeRegistry registry, string eventsRepo, TimeProvider clock, Random random)
    {
        this.registry = registry;
        this.eventsRepo = eventsRepo;
        this.clock = clock;
        this.random = random;
    }

    public string Name => "ingest-graph-metrics";
    public JobCadence Cadence => JobCadence.Daily;

    public string Run()
    {
        List<KnowledgeSource> publishing = registry.Sources
            .Where(source => source.MetricsArtifact is not null)
            .ToList();
        if (publishing.Count == 0)
        {
            return "no source carries metricsArtifact — nothing to ingest";
        }

        BronzeStore store = new(eventsRepo);
        HashSet<string> seenKeys = new(store.GraphMetricsKeys());
        EventValidator validator = new();

        List<JsonObject> pending = new();
        List<string> absent = new();
        int skipped = 0;

        foreach (KnowledgeSource source in publishing)
        {
            string artifact = source.MetricsArtifact!;
            if (!File.Exists(artifact))
            {
                absent.Add(source.Id);
                continue;
            }

            IngestArtifact(artifact, source, validator, seenKeys, pending, ref skipped);
        }

        if (pending.Count > 0)
        {
            store.Append(pending);
        }

        string summary = $"ingested {pending.Count} graph.metrics event(s), skipped {skipped} duplicate(s)";
        if (absent.Count > 0)
        {
            summary += $"; artifact absent: {string.Join(", ", absent)}";
        }
        return summary;
    }

    private void IngestArtifact(
        string artifact,
        KnowledgeSource source,
        EventValidator validator,
        HashSet<string> seenKeys,
        List<JsonObject> pending,
        ref int skipped)
    {
        int lineNumber = 0;
        foreach (string line in File.ReadLines(artifact))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonObject? parsed;
            try
            {
                parsed = JsonNode.Parse(line) as JsonObject;
            }
            catch (System.Text.Json.JsonException exception)
            {
                throw new InvalidOperationException(
                    $"{artifact} line {lineNumber}: not valid JSON ({exception.Message})");
            }

            JsonObject payload = parsed
                ?? throw new InvalidOperationException($"{artifact} line {lineNumber}: not a JSON object");

            string? payloadSource = (string?)payload[EventDataFields.Source];
            if (payloadSource != source.Id)
            {
                throw new InvalidOperationException(
                    $"{artifact} line {lineNumber}: payload source '{payloadSource ?? "null"}' does not match the registry row '{source.Id}'");
            }

            JsonObject envelope = EventEnvelope.Create(
                EventTypes.GraphMetrics,
                subject: source.Id,
                kbroot: source.Id,
                data: payload,
                registry.Machine,
                PulseRunner.AgentName,
                session: null,
                repo: null,
                task: null,
                model: null,
                clock.GetUtcNow(),
                random);

            EventValidationResult validation = validator.Validate(envelope.ToJsonString());
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(
                    $"{artifact} line {lineNumber}: schema violation: {string.Join("; ", validation.Errors)}");
            }

            string dedupKey = (string?)payload[EventDataFields.Date] + "|" + payloadSource;
            if (!seenKeys.Add(dedupKey))
            {
                skipped++;
                continue;
            }

            pending.Add(envelope);
        }
    }
}
