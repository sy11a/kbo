using DuckDB.NET.Data;
using Kbo.Registry;
using Kbo.Silver;

namespace Kbo.Gold;

public static class GoldComputer
{
    public const int MinInventoryAgeDays = 30;
    public const int ReadWindowDays = 60;
    public const int StaleMinReads = 3;
    public const int StaleUnmodifiedDays = 90;
    public const int HotNoteLimit = 20;
    public const int DormantAfterDays = 21;

    private static readonly string[] NoteActions = ["archive", "merge", "re-link"];
    private static readonly string[] SkillActions = ["retire", "fix trigger phrases"];

    private sealed record ReadStats(long ReadsInWindow, long ReadsTotal, DateTimeOffset LastRead);

    public static GoldReport Compute(string silverPath, KnowledgeRegistry registry, TimeProvider clock)
    {
        DateTimeOffset now = clock.GetUtcNow();
        Dictionary<string, ReadStats> statsByPath = QueryReadStats(silverPath, now.AddDays(-ReadWindowDays));
        List<InventoryNote> inventory = NoteInventory.Scan(registry);

        Dictionary<string, int> inventoryCounts = inventory
            .GroupBy(note => note.SourceId)
            .ToDictionary(group => group.Key, group => group.Count());

        List<DeadNote> deadNotes = new();
        List<StaleNote> staleNotes = new();
        Dictionary<string, int> lifecycleCounts = new();
        Dictionary<string, int> machineManagedCounts = new();
        foreach (InventoryNote note in inventory)
        {
            string role = NoteRole.Of(note.Path);
            if (role == NoteRole.Lifecycle)
            {
                lifecycleCounts[note.SourceId] = lifecycleCounts.GetValueOrDefault(note.SourceId) + 1;
            }
            if (role == NoteRole.MachineManaged)
            {
                machineManagedCounts[note.SourceId] = machineManagedCounts.GetValueOrDefault(note.SourceId) + 1;
            }

            int daysSinceModified = (int)(now - note.Modified).TotalDays;
            ReadStats? stats = statsByPath.GetValueOrDefault(note.Path);
            long readsInWindow = stats?.ReadsInWindow ?? 0;

            if (role == NoteRole.Reference && daysSinceModified >= MinInventoryAgeDays && readsInWindow == 0)
            {
                string[] actions = note.Layer == KnowledgeLayer.Skills ? SkillActions : NoteActions;
                deadNotes.Add(new DeadNote(note.Path, note.SourceId, LayerName(note.Layer), daysSinceModified, stats?.LastRead, actions));
            }

            if (readsInWindow >= StaleMinReads && daysSinceModified > StaleUnmodifiedDays)
            {
                staleNotes.Add(new StaleNote(note.Path, note.SourceId, readsInWindow, daysSinceModified));
            }
        }

        Dictionary<string, InventoryNote> inventoryByPath = inventory.ToDictionary(note => note.Path);
        List<HotNote> hotNotes = statsByPath
            .Where(entry => entry.Value.ReadsInWindow > 0 && inventoryByPath.ContainsKey(entry.Key))
            .OrderByDescending(entry => entry.Value.ReadsInWindow)
            .ThenByDescending(entry => entry.Value.ReadsTotal)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(HotNoteLimit)
            .Select(entry => new HotNote(
                entry.Key,
                inventoryByPath[entry.Key].SourceId,
                entry.Value.ReadsInWindow,
                entry.Value.ReadsTotal,
                entry.Value.LastRead))
            .ToList();

        Dictionary<string, DateTimeOffset> activityBySource = QuerySourceActivity(silverPath, registry);
        DateTimeOffset dormantCutoff = now.AddDays(-DormantAfterDays);
        HashSet<string> dormantSourceIds = inventoryCounts.Keys
            .Where(id => !activityBySource.TryGetValue(id, out DateTimeOffset last) || last < dormantCutoff)
            .ToHashSet();

        List<DormantSource> dormantSources = dormantSourceIds
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => new DormantSource(
                id,
                activityBySource.TryGetValue(id, out DateTimeOffset last) ? last : null,
                deadNotes.Count(note => note.SourceId == id)))
            .ToList();

        deadNotes = deadNotes.Where(note => !dormantSourceIds.Contains(note.SourceId)).ToList();

        return new GoldReport(
            now,
            registry.Machine,
            MinInventoryAgeDays,
            ReadWindowDays,
            StaleMinReads,
            StaleUnmodifiedDays,
            DormantAfterDays,
            inventoryCounts,
            lifecycleCounts,
            machineManagedCounts,
            dormantSources,
            deadNotes.OrderBy(note => note.Path, StringComparer.Ordinal).ToList(),
            hotNotes,
            staleNotes.OrderByDescending(note => note.ReadsInWindow).ThenBy(note => note.Path, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Last *usage* per source (practice reads + context loads only): a write
    /// alone — e.g. a fleet-wide maintenance stamp — never proves a source is
    /// alive (ADR-0035), and neither does a service session's read — a fleet
    /// rollout must not wake a paused project (ADR-0039).
    /// </summary>
    private static Dictionary<string, DateTimeOffset> QuerySourceActivity(string silverPath, KnowledgeRegistry registry)
    {
        Dictionary<string, DateTimeOffset> lastBySource = new();
        void Bump(string sourceId, DateTimeOffset time)
        {
            if (!lastBySource.TryGetValue(sourceId, out DateTimeOffset existing) || time > existing)
            {
                lastBySource[sourceId] = time;
            }
        }

        using DuckDBConnection connection = SilverConnection.OpenReadOnly(silverPath);

        using (DuckDBCommand bySubject = connection.CreateCommand())
        {
            bySubject.CommandText = """
                SELECT subject, max(time) FROM practice_events
                WHERE type IN ('knowledge.read', 'context.loaded')
                  AND subject IS NOT NULL GROUP BY subject
                """;
            using DuckDBDataReader reader = (DuckDBDataReader)bySubject.ExecuteReader();
            while (reader.Read())
            {
                string? sourceId = registry.Resolve(reader.GetString(0));
                if (sourceId is not null)
                {
                    Bump(sourceId, new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc)));
                }
            }
        }

        using (DuckDBCommand byRepo = connection.CreateCommand())
        {
            byRepo.CommandText = """
                SELECT repo, max(time) FROM practice_events
                WHERE type IN ('knowledge.read', 'context.loaded')
                  AND repo IS NOT NULL GROUP BY repo
                """;
            using DuckDBDataReader reader = (DuckDBDataReader)byRepo.ExecuteReader();
            while (reader.Read())
            {
                string repo = reader.GetString(0);
                DateTimeOffset time = new(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));
                foreach (KnowledgeSource source in registry.Sources)
                {
                    if (source.Root == repo || Path.GetDirectoryName(source.Root) == repo)
                    {
                        Bump(source.Id, time);
                    }
                }
            }
        }

        return lastBySource;
    }

    private static string LayerName(KnowledgeLayer layer)
    {
        return layer.ToString().ToLowerInvariant();
    }

    private static Dictionary<string, ReadStats> QueryReadStats(string silverPath, DateTimeOffset windowCutoff)
    {
        Dictionary<string, ReadStats> statsByPath = new();

        using DuckDBConnection connection = SilverConnection.OpenReadOnly(silverPath);
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT subject,
                   count(*) FILTER (WHERE time >= ?) AS reads_in_window,
                   count(*) AS reads_total,
                   max(time) AS last_read
            FROM practice_events
            WHERE type IN ('knowledge.read', 'context.loaded')
              AND subject IS NOT NULL
            GROUP BY subject
            """;
        command.Parameters.Add(new DuckDBParameter { Value = windowCutoff.UtcDateTime });

        using DuckDBDataReader reader = (DuckDBDataReader)command.ExecuteReader();
        while (reader.Read())
        {
            DateTime lastRead = DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc);
            statsByPath[reader.GetString(0)] = new ReadStats(
                reader.GetInt64(1),
                reader.GetInt64(2),
                new DateTimeOffset(lastRead));
        }
        return statsByPath;
    }
}
