using System.Globalization;
using System.Text;

namespace Kbo.Gold;

/// <summary>
/// GoldReport → Markdown. Renders what gold computed — zero computation (P2).
/// </summary>
internal static class MarkdownRenderer
{
    public static string Render(GoldReport report, string vaultRoot)
    {
        StringBuilder markdown = new();
        AppendTitle(markdown, report);
        AppendInventory(markdown, report);
        AppendLifecycle(markdown, report);
        AppendMachineManaged(markdown, report);
        AppendDormantSources(markdown, report);
        AppendDeadNotes(markdown, report, vaultRoot);
        AppendHotNotes(markdown, report, vaultRoot);
        AppendStaleness(markdown, report, vaultRoot);
        return markdown.ToString();
    }

    private static void AppendTitle(StringBuilder markdown, GoldReport report)
    {
        _ = markdown.AppendLine("# kbo report — knowledge worklists");
        _ = markdown.AppendLine();
        _ = markdown.AppendLine(CultureInfo.InvariantCulture, $"> **GENERATED** by `kbo report` at **{Timestamp(report.GeneratedAt)}** on `{report.Machine}` — hand-edits die on the next run.");
        _ = markdown.AppendLine();
    }

    private static void AppendInventory(StringBuilder markdown, GoldReport report)
    {
        _ = markdown.AppendLine("## Inventory");
        _ = markdown.AppendLine();
        foreach (KeyValuePair<string, int> entry in report.InventoryCounts.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            _ = markdown.AppendLine(CultureInfo.InvariantCulture, $"- `{entry.Key}`: {entry.Value} note(s)");
        }
        _ = markdown.AppendLine();
    }

    private static void AppendLifecycle(StringBuilder markdown, GoldReport report)
    {
        _ = markdown.AppendLine("## Lifecycle artifacts — die on completion, excluded from the dead worklist");
        _ = markdown.AppendLine();
        if (report.LifecycleCounts.Count is 0)
        {
            _ = markdown.AppendLine("none");
        }
        else
        {
            foreach (KeyValuePair<string, int> entry in report.LifecycleCounts.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                _ = markdown.AppendLine(CultureInfo.InvariantCulture, $"- `{entry.Key}`: {entry.Value} note(s) (plans / specs / journal)");
            }
        }
        _ = markdown.AppendLine();
    }

    private static void AppendMachineManaged(StringBuilder markdown, GoldReport report)
    {
        _ = markdown.AppendLine("## Machine-managed files — tool-owned, excluded from the dead worklist");
        _ = markdown.AppendLine();
        if (report.MachineManagedCounts.Count is 0)
        {
            _ = markdown.AppendLine("none");
        }
        else
        {
            foreach (KeyValuePair<string, int> entry in report.MachineManagedCounts.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                _ = markdown.AppendLine(CultureInfo.InvariantCulture, $"- `{entry.Key}`: {entry.Value} file(s) (fleet law / templates)");
            }
        }
        _ = markdown.AppendLine();
    }

    private static void AppendDormantSources(StringBuilder markdown, GoldReport report)
    {
        _ = markdown.AppendLine(CultureInfo.InvariantCulture, $"## Dormant sources — no activity in {report.DormantAfterDays}d, dead-note check suspended");
        _ = markdown.AppendLine();
        if (report.DormantSources.Count is 0)
        {
            _ = markdown.AppendLine("none");
        }
        else
        {
            _ = markdown.AppendLine("| Source | Last activity | Dead notes withheld |");
            _ = markdown.AppendLine("|--------|---------------|--------------------:|");
            foreach (DormantSource source in report.DormantSources)
            {
                string lastActivity = source.LastActivity is null ? "never" : Timestamp(source.LastActivity.Value);
                _ = markdown.AppendLine(CultureInfo.InvariantCulture,
                    $"| `{source.SourceId}` | {lastActivity} | {source.WithheldDeadNotes} |");
            }
        }
        _ = markdown.AppendLine();
    }

    private static void AppendDeadNotes(StringBuilder markdown, GoldReport report, string vaultRoot)
    {
        _ = markdown.AppendLine(CultureInfo.InvariantCulture, $"## Dead notes — in inventory ≥ {report.MinInventoryAgeDays}d, zero reads in {report.ReadWindowDays}d");
        _ = markdown.AppendLine();
        if (report.DeadNotes.Count is 0)
        {
            _ = markdown.AppendLine("none 🎉");
        }
        else
        {
            _ = markdown.AppendLine("| Note | Source | Unmodified | Last read | Suggested |");
            _ = markdown.AppendLine("|------|--------|-----------:|-----------|-----------|");
            foreach (DeadNote note in report.DeadNotes)
            {
                string lastRead = note.LastRead is null ? "never" : Timestamp(note.LastRead.Value);
                _ = markdown.AppendLine(CultureInfo.InvariantCulture,
                    $"| {Link(note.Path, vaultRoot)} | {note.SourceId} | {note.DaysSinceModified}d | {lastRead} | {string.Join(" / ", note.SuggestedActions)} |");
            }
        }
        _ = markdown.AppendLine();
    }

    private static void AppendHotNotes(StringBuilder markdown, GoldReport report, string vaultRoot)
    {
        _ = markdown.AppendLine(CultureInfo.InvariantCulture, $"## Hot notes — top reads in the last {report.ReadWindowDays}d");
        _ = markdown.AppendLine();
        if (report.HotNotes.Count is 0)
        {
            _ = markdown.AppendLine("none");
        }
        else
        {
            _ = markdown.AppendLine("| Note | Source | Reads (window) | Reads (total) | Last read |");
            _ = markdown.AppendLine("|------|--------|---------------:|--------------:|-----------|");
            foreach (HotNote note in report.HotNotes)
            {
                _ = markdown.AppendLine(CultureInfo.InvariantCulture,
                    $"| {Link(note.Path, vaultRoot)} | {note.SourceId} | {note.ReadsInWindow} | {note.ReadsTotal} | {Timestamp(note.LastRead)} |");
            }
        }
        _ = markdown.AppendLine();
    }

    private static void AppendStaleness(StringBuilder markdown, GoldReport report, string vaultRoot)
    {
        _ = markdown.AppendLine(CultureInfo.InvariantCulture, $"## Staleness — ≥ {report.StaleMinReads} reads in {report.ReadWindowDays}d, unmodified > {report.StaleUnmodifiedDays}d");
        _ = markdown.AppendLine();
        if (report.StaleNotes.Count is 0)
        {
            _ = markdown.AppendLine("none");
        }
        else
        {
            _ = markdown.AppendLine("| Note | Source | Reads (window) | Unmodified |");
            _ = markdown.AppendLine("|------|--------|---------------:|-----------:|");
            foreach (StaleNote note in report.StaleNotes)
            {
                _ = markdown.AppendLine(CultureInfo.InvariantCulture,
                    $"| {Link(note.Path, vaultRoot)} | {note.SourceId} | {note.ReadsInWindow} | {note.DaysSinceModified}d |");
            }
        }
    }

    private static string Link(string path, string vaultRoot)
    {
        string prefix = vaultRoot.EndsWith('/') ? vaultRoot : vaultRoot + "/";
        if (path.StartsWith(prefix, StringComparison.Ordinal))
        {
            string relative = path[prefix.Length..];
            if (relative.EndsWith(".md", StringComparison.Ordinal))
            {
                relative = relative[..^".md".Length];
            }
            return $"[[{relative}]]";
        }
        return $"`{path}`";
    }

    private static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
