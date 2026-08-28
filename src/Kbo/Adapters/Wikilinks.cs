using System.Text.RegularExpressions;

namespace Kbo.Adapters;

/// <summary>
/// Counts the distinct normalized wikilink targets in a note body (BL-038):
/// the alias part (<c>target|alias</c>) and the anchor part
/// (<c>target#anchor</c>) are stripped, duplicate targets count once, and
/// transclusion embeds (<c>![[target]]</c>) count as references. Pure, no
/// I/O — the live adapters feed it the file content they read after a write.
/// </summary>
public static class Wikilinks
{
    private static readonly Regex Target = new(@"\[\[([^\[\]]+)\]\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static int CountDistinct(string content)
    {
        HashSet<string> targets = new(StringComparer.Ordinal);
        foreach (Match match in Target.Matches(content))
        {
            string target = match.Groups[1].Value;
            int aliasSeparator = target.IndexOf('|');
            if (aliasSeparator >= 0)
            {
                target = target[..aliasSeparator];
            }
            int anchorSeparator = target.IndexOf('#');
            if (anchorSeparator >= 0)
            {
                target = target[..anchorSeparator];
            }
            target = target.Trim();
            if (target.Length > 0)
            {
                targets.Add(target);
            }
        }
        return targets.Count;
    }
}
