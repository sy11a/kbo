using Kbo.Schemas;

namespace Kbo.Tests;

public class EventValidatorTests
{
    private static readonly EventValidator Validator = new();

    private const string ValidKnowledgeRead = """
        {"specversion":"1.0","id":"01J2ZK8Q000000000000000001","source":"//test-machine/claude-code","type":"knowledge.read","time":"2026-08-10T12:00:00Z","subject":"~/Knowledge/notes/duckdb.md","data":{"path":"~/Knowledge/notes/duckdb.md","contenthash":"a1b2c3d4e5f60718","raw":{"tool_name":"Read","tool_input":{"file_path":"~/Knowledge/notes/duckdb.md"}}},"machine":"test-machine","agent":"claude-code","session":"synthetic-session-0001","repo":"~/Repository/example","task":"AC-123","model":"claude-fable-5","kbroot":"vault","schemaref":"knowledge.read/1"}
        """;

    [Fact]
    public void Valid_knowledge_read_event_passes()
    {
        EventValidationResult result = Validator.Validate(ValidKnowledgeRead);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Event_missing_required_envelope_field_fails()
    {
        string missingMachine = ValidKnowledgeRead.Replace("\"machine\":\"test-machine\",", "");

        EventValidationResult result = Validator.Validate(missingMachine);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }
}
