namespace Kbo.Schemas;

/// <summary>
/// JSON property names of the event envelope (ADR-0001, schemas/envelope/1.json).
/// </summary>
public static class EnvelopeFields
{
    public const string SpecVersion = "specversion";
    public const string Id = "id";
    public const string Source = "source";
    public const string Type = "type";
    public const string Time = "time";
    public const string Subject = "subject";
    public const string Data = "data";
    public const string Machine = "machine";
    public const string Agent = "agent";
    public const string Session = "session";
    public const string Repo = "repo";
    public const string Task = "task";
    public const string Model = "model";
    public const string Kbroot = "kbroot";
    public const string SchemaRef = "schemaref";

    public const string SpecVersionValue = "1.0";
}
