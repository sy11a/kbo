namespace Kbo.Schemas;

internal sealed record EventValidationResult(bool IsValid, string? SchemaRef, IReadOnlyList<string> Errors)
{
    public static EventValidationResult Valid(string schemaRef) => new(IsValid: true, schemaRef, []);

    public static EventValidationResult Invalid(string? schemaRef, params string[] errors) => new(IsValid: false, schemaRef, errors);
}
