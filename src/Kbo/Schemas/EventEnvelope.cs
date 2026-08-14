using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;

namespace Kbo.Schemas;

/// <summary>
/// The single constructor for envelope events (ADR-0001) — live capture and
/// harvest both emit through here so the two origins cannot drift.
/// </summary>
public static class EventEnvelope
{
    public static JsonObject Create(
        string type,
        string? subject,
        string? kbroot,
        JsonObject data,
        string machine,
        string agent,
        string? session,
        string? repo,
        string? task,
        string? model,
        DateTimeOffset time,
        Random random)
    {
        return new JsonObject
        {
            [EnvelopeFields.SpecVersion] = EnvelopeFields.SpecVersionValue,
            [EnvelopeFields.Id] = Ulid.New(time, random),
            [EnvelopeFields.Source] = $"//{machine}/{agent}",
            [EnvelopeFields.Type] = type,
            [EnvelopeFields.Time] = time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            [EnvelopeFields.Subject] = subject,
            [EnvelopeFields.Data] = data,
            [EnvelopeFields.Machine] = machine,
            [EnvelopeFields.Agent] = agent,
            [EnvelopeFields.Session] = session,
            [EnvelopeFields.Repo] = repo,
            [EnvelopeFields.Task] = task,
            [EnvelopeFields.Model] = model,
            [EnvelopeFields.Kbroot] = kbroot,
            [EnvelopeFields.SchemaRef] = EventTypes.V1SchemaRef(type),
        };
    }
}
