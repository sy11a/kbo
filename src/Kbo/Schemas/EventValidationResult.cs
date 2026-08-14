namespace Kbo.Schemas;

public sealed record EventValidationResult(bool IsValid, string? SchemaRef, IReadOnlyList<string> Errors)
{
    public static EventValidationResult Valid(string schemaRef)
    {
        return new EventValidationResult(true, schemaRef, []);
    }

    public static EventValidationResult Invalid(string? schemaRef, params string[] errors)
    {
        return new EventValidationResult(false, schemaRef, errors);
    }
}
