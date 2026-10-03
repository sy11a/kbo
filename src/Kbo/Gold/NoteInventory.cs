using Kbo.Registry;

namespace Kbo.Gold;

internal sealed record InventoryNote(string Path, string SourceId, KnowledgeLayer Layer, DateTimeOffset Modified);

internal static class NoteInventory
{
    public const string NotePattern = "*.md";

    public static List<InventoryNote> Scan(KnowledgeRegistry registry)
    {
        List<InventoryNote> notes = [];
        foreach (KnowledgeSource source in registry.Sources)
        {
            if (!Directory.Exists(source.Root))
            {
                continue;
            }
            string[] excludedPrefixes = [.. source.ExcludePaths.Select(excludePath => Path.Combine(source.Root, excludePath) + "/")];
            foreach (string path in Directory.EnumerateFiles(source.Root, NotePattern, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                if (excludedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    continue;
                }
                notes.Add(new InventoryNote(path, source.Id, source.Layer, File.GetLastWriteTimeUtc(path)));
            }
        }
        return notes;
    }
}
