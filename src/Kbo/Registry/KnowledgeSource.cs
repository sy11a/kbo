namespace Kbo.Registry;

public sealed record KnowledgeSource(string Id, KnowledgeLayer Layer, string Root)
{
    /// <summary>
    /// Relative subtrees under Root excluded from the note inventory
    /// (tool fixtures, benchmark data — ADR-0036). Inventory-only: path
    /// resolution and kbroot tagging are unaffected.
    /// </summary>
    public IReadOnlyList<string> ExcludePaths { get; init; } = [];

    /// <summary>
    /// Optional path to a sibling repo's corpus-aggregate export artifact
    /// (NDJSON of graph.metrics/1 data payloads — kbl ADR-0006 touchpoint 4).
    /// Null on every source that publishes no artifact. Ingest-pointer only:
    /// resolution and kbroot tagging are unaffected.
    /// </summary>
    public string? MetricsArtifact { get; init; }
}
