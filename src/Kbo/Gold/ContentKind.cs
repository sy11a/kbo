namespace Kbo.Gold;

/// <summary>
/// Classifies a read subject by file kind so metrics can separate actual
/// knowledge notes from source code and config that whole-repo registration
/// also sweeps in (ADR-0025). Extension-based; pure, no I/O.
/// </summary>
public static class ContentKind
{
    public const string Knowledge = "knowledge";
    public const string Code = "code";
    public const string Config = "config";
    public const string Other = "other";

    private static readonly HashSet<string> KnowledgeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdx", ".txt", ".rst", ".org", ".adoc",
    };

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".rs", ".java", ".rb",
        ".cpp", ".cc", ".c", ".h", ".hpp", ".sh", ".ps1", ".sql", ".razor", ".vue",
        ".php", ".kt", ".swift", ".scala", ".ex", ".exs", ".lua", ".r",
    };

    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".yaml", ".yml", ".toml", ".ini", ".env", ".xml", ".tpl", ".config",
        ".props", ".targets", ".csproj", ".editorconfig", ".gradle", ".lock",
    };

    public static string Of(string path)
    {
        string extension = Path.GetExtension(path);
        if (KnowledgeExtensions.Contains(extension))
        {
            return Knowledge;
        }
        if (CodeExtensions.Contains(extension))
        {
            return Code;
        }
        if (ConfigExtensions.Contains(extension))
        {
            return Config;
        }
        return Other;
    }
}
