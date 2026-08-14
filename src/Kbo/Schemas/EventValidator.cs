using System.Reflection;
using System.Text.Json;
using Json.Schema;

namespace Kbo.Schemas;

/// <summary>
/// Validates one NDJSON event line against the schema registry embedded from
/// <c>schemas/&lt;type&gt;/&lt;version&gt;.json</c>. The event's <c>schemaref</c>
/// field selects the schema (ADR-0001/ADR-0002).
/// </summary>
public sealed class EventValidator
{
    private const string ResourcePrefix = "schemas/";
    private const string EnvelopePrefix = "envelope/";

    private readonly Dictionary<string, JsonSchema> schemasByRef;
    private readonly EvaluationOptions evaluationOptions;

    public EventValidator()
    {
        BuildOptions buildOptions = new() { SchemaRegistry = new SchemaRegistry() };
        schemasByRef = new Dictionary<string, JsonSchema>();

        Assembly assembly = typeof(EventValidator).Assembly;
        List<string> resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .OrderBy(name => name.StartsWith(ResourcePrefix + EnvelopePrefix, StringComparison.Ordinal) ? 0 : 1)
            .ToList();

        foreach (string resourceName in resourceNames)
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded schema resource '{resourceName}' could not be opened.");
            using StreamReader reader = new(stream);
            JsonSchema schema = JsonSchema.FromText(reader.ReadToEnd(), buildOptions);
            buildOptions.SchemaRegistry.Register(schema);

            string schemaRef = resourceName[ResourcePrefix.Length..^".json".Length].Replace('\\', '/');
            if (!schemaRef.StartsWith(EnvelopePrefix, StringComparison.Ordinal))
            {
                schemasByRef[schemaRef] = schema;
            }
        }

        evaluationOptions = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true
        };
    }

    public IReadOnlyCollection<string> KnownSchemaRefs => schemasByRef.Keys;

    public EventValidationResult Validate(string eventJsonLine)
    {
        using JsonDocument eventDocument = ParseOrNull(eventJsonLine, out string? parseError);
        if (parseError is not null)
        {
            return EventValidationResult.Invalid(null, $"Not valid JSON: {parseError}");
        }

        JsonElement root = eventDocument.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(EnvelopeFields.SchemaRef, out JsonElement schemaRefElement)
            || schemaRefElement.ValueKind != JsonValueKind.String)
        {
            return EventValidationResult.Invalid(null, "Event has no string 'schemaref' field; cannot select a schema.");
        }

        string schemaRef = schemaRefElement.GetString()!;
        if (!schemasByRef.TryGetValue(schemaRef, out JsonSchema? schema))
        {
            return EventValidationResult.Invalid(schemaRef, $"Unknown schemaref '{schemaRef}': no schema file 'schemas/{schemaRef}.json' in the registry.");
        }

        EvaluationResults evaluation = schema.Evaluate(root, evaluationOptions);
        if (evaluation.IsValid)
        {
            return EventValidationResult.Valid(schemaRef);
        }

        string[] errors = (evaluation.Details ?? [])
            .Where(detail => detail.Errors is { Count: > 0 })
            .SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}"))
            .ToArray();
        return EventValidationResult.Invalid(schemaRef, errors.Length > 0 ? errors : ["Event does not conform to the schema."]);
    }

    private static JsonDocument ParseOrNull(string json, out string? parseError)
    {
        try
        {
            parseError = null;
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            parseError = exception.Message;
            return JsonDocument.Parse("null");
        }
    }
}
