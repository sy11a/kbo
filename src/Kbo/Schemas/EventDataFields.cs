namespace Kbo.Schemas;

/// <summary>
/// JSON property names inside an event's <c>data</c> object, per the type
/// schemas in <c>schemas/&lt;type&gt;/1.json</c>.
/// </summary>
public static class EventDataFields
{
    public const string Path = "path";
    public const string ContentHash = "contenthash";
    public const string Size = "size";
    public const string Raw = "raw";
    public const string Pattern = "pattern";
    public const string Root = "root";
    public const string Hits = "hits";
    public const string Branch = "branch";
    public const string Usage = "usage";
    public const string Kind = "kind";
    public const string Skill = "skill";
    public const string Origin = "origin";
    public const string Transcript = "transcript";
    public const string Job = "job";
    public const string DurationMs = "duration_ms";
    public const string Error = "error";

    public const string OriginHook = "hook";
    public const string OriginHarvest = "harvest";
}
