using System.Globalization;
using System.Text;

namespace Kbo.Gold;

/// <summary>
/// GoldReport → Markdown. Renders what gold computed — zero computation (P2).
/// </summary>
public static class MarkdownRenderer
{
    public static string Render(GoldReport report, string vaultRoot)
    {
        StringBuilder markdown = new();
        markdown.AppendLine("# kbo report — knowledge worklists");
        markdown.AppendLine();
        markdown.AppendLine(CultureInfo.InvariantCulture, $"> **GENERATED** by `kbo report` at **{Timestamp(report.GeneratedAt)}** on `{report.Machine}` — hand-edits die on the next run.");
        markdown.AppendLine();
        markdown.AppendLine("## Inventory");
        markdown.AppendLine();
        foreach (KeyValuePair<string, int> entry in report.InventoryCounts.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            markdown.AppendLine(CultureInfo.InvariantCulture, $"- `{entry.Key}`: {entry.Value} note(s)");
        }
        markdown.AppendLine();

        markdown.AppendLine("## Lifecycle artifacts — die on completion, excluded from the dead worklist");
        markdown.AppendLine();
        if (report.LifecycleCounts.Count == 0)
        {
            markdown.AppendLine("none");
        }
        else
        {
            foreach (KeyValuePair<string, int> entry in report.LifecycleCounts.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                markdown.AppendLine(CultureInfo.InvariantCulture, $"- `{entry.Key}`: {entry.Value} note(s) (plans / specs / journal)");
            }
        }
        markdown.AppendLine();

        markdown.AppendLine(CultureInfo.InvariantCulture, $"## Dead notes — in inventory ≥ {report.MinInventoryAgeDays}d, zero reads in {report.ReadWindowDays}d");
        markdown.AppendLine();
        if (report.DeadNotes.Count == 0)
        {
            markdown.AppendLine("none 🎉");
        }
        else
        {
            markdown.AppendLine("| Note | Source | Unmodified | Last read | Suggested |");
            markdown.AppendLine("|------|--------|-----------:|-----------|-----------|");
            foreach (DeadNote note in report.DeadNotes)
            {
                string lastRead = note.LastRead is null ? "never" : Timestamp(note.LastRead.Value);
                markdown.AppendLine(CultureInfo.InvariantCulture,
                    $"| {Link(note.Path, vaultRoot)} | {note.SourceId} | {note.DaysSinceModified}d | {lastRead} | {string.Join(" / ", note.SuggestedActions)} |");
            }
        }
        markdown.AppendLine();

        markdown.AppendLine(CultureInfo.InvariantCulture, $"## Hot notes — top reads in the last {report.ReadWindowDays}d");
        markdown.AppendLine();
        if (report.HotNotes.Count == 0)
        {
            markdown.AppendLine("none");
        }
        else
        {
            markdown.AppendLine("| Note | Source | Reads (window) | Reads (total) | Last read |");
            markdown.AppendLine("|------|--------|---------------:|--------------:|-----------|");
            foreach (HotNote note in report.HotNotes)
            {
                markdown.AppendLine(CultureInfo.InvariantCulture,
                    $"| {Link(note.Path, vaultRoot)} | {note.SourceId} | {note.ReadsInWindow} | {note.ReadsTotal} | {Timestamp(note.LastRead)} |");
            }
        }
        markdown.AppendLine();

        markdown.AppendLine(CultureInfo.InvariantCulture, $"## Staleness — ≥ {report.StaleMinReads} reads in {report.ReadWindowDays}d, unmodified > {report.StaleUnmodifiedDays}d");
        markdown.AppendLine();
        if (report.StaleNotes.Count == 0)
        {
            markdown.AppendLine("none");
        }
        else
        {
            markdown.AppendLine("| Note | Source | Reads (window) | Unmodified |");
            markdown.AppendLine("|------|--------|---------------:|-----------:|");
            foreach (StaleNote note in report.StaleNotes)
            {
                markdown.AppendLine(CultureInfo.InvariantCulture,
                    $"| {Link(note.Path, vaultRoot)} | {note.SourceId} | {note.ReadsInWindow} | {note.DaysSinceModified}d |");
            }
        }

        return markdown.ToString();
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

    private static string Timestamp(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
