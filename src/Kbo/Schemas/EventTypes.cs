namespace Kbo.Schemas;

/// <summary>
/// Event type names from the taxonomy registry (docs/events.md) that the code
/// currently emits; job.* joins when pulse ships.
/// </summary>
public static class EventTypes
{
    public const string KnowledgeRead = "knowledge.read";
    public const string KnowledgeSearched = "knowledge.searched";
    public const string KnowledgeWritten = "knowledge.written";
    public const string ContextLoaded = "context.loaded";
    public const string SkillInvoked = "skill.invoked";
    public const string SessionStarted = "session.started";
    public const string JobCompleted = "job.completed";
    public const string JobFailed = "job.failed";

    public static string V1SchemaRef(string type)
    {
        return $"{type}/1";
    }
}
