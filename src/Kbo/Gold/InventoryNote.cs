using Kbo.Registry;

namespace Kbo.Gold;

internal sealed record InventoryNote(string Path, string SourceId, KnowledgeLayer Layer, DateTimeOffset Modified);
