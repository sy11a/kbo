namespace Kbo.Registry;

public sealed record KnowledgeSource(string Id, KnowledgeLayer Layer, string Root)
{
    /// <summary>
    /// Relative subtrees under Root excluded from the note inventory
    /// (tool fixtures, benchmark data — ADR-0036). Inventory-only: path
    /// resolution and kbroot tagging are unaffected.
    /// </summary>
    public IReadOnlyList<string> ExcludePaths { get; init; } = [];
}
