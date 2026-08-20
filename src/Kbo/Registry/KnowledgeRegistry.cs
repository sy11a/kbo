using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kbo.Registry;

public sealed class KnowledgeRegistry
{
    public string Machine { get; }
    public IReadOnlyList<KnowledgeSource> Sources { get; }

    /// <summary>
    /// Optional branch → task-id regex (first match wins). Null means no task
    /// extraction: a public tool ships no default ticket convention (ADR-0031).
    /// </summary>
    public Regex? TaskPattern { get; }

    /// <summary>
    /// Optional legislator wiring for the constitution-fleet panel
    /// (ADR-0038). Null means no fleet tracking: a public tool ships no
    /// default legislator location (ADR-0031 pattern).
    /// </summary>
    public ConstitutionConfig? Constitution { get; }

    /// <summary>
    /// Optional SDD-panel wiring (ADR-0040): skill names counted as
    /// spec/plan-writing practice. Null means the skill-rate metric is
    /// unconfigured (stated on the dashboard, never silently omitted).
    /// </summary>
    public SddConfig? Sdd { get; }

    private KnowledgeRegistry(string machine, IReadOnlyList<KnowledgeSource> sources, Regex? taskPattern,
        ConstitutionConfig? constitution, SddConfig? sdd)
    {
        Machine = machine;
        Sources = sources;
        TaskPattern = taskPattern;
        Constitution = constitution;
        Sdd = sdd;
    }

    public string? Resolve(string path)
    {
        string normalizedPath = path.Length > 1 ? path.TrimEnd('/') : path;

        KnowledgeSource? bestMatch = null;
        foreach (KnowledgeSource source in Sources)
        {
            bool contains = normalizedPath == source.Root
                || normalizedPath.StartsWith(source.Root + "/", StringComparison.Ordinal);
            if (contains && (bestMatch is null || source.Root.Length > bestMatch.Root.Length))
            {
                bestMatch = source;
            }
        }

        return bestMatch?.Id;
    }

    public static KnowledgeRegistry Load(string path, string? taskPatternOverride = null)
    {
        if (!File.Exists(path))
        {
            throw new RegistryFormatException($"registry file not found: {path}");
        }

        return Parse(File.ReadAllText(path), taskPatternOverride);
    }

    public static KnowledgeRegistry Parse(string yaml, string? taskPatternOverride = null)
    {
        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        RegistryDocument document;
        try
        {
            document = deserializer.Deserialize<RegistryDocument>(yaml);
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            throw new RegistryFormatException($"registry is not valid YAML: {exception.Message}", exception);
        }

        List<string> errors = new();
        if (document is null || string.IsNullOrWhiteSpace(document.Machine))
        {
            errors.Add("'machine' is missing");
        }
        if (document?.Sources is null || document.Sources.Count == 0)
        {
            errors.Add("'sources' is missing or empty");
        }

        List<KnowledgeSource> sources = new();
        HashSet<string> seenIds = new();
        foreach (SourceEntry entry in document?.Sources ?? new List<SourceEntry>())
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                errors.Add("a source is missing 'id'");
                continue;
            }
            if (!seenIds.Add(entry.Id))
            {
                errors.Add($"duplicate source id '{entry.Id}'");
            }
            if (string.IsNullOrWhiteSpace(entry.Layer))
            {
                errors.Add($"source '{entry.Id}': 'layer' is missing");
                continue;
            }
            if (!Enum.TryParse(entry.Layer, ignoreCase: true, out KnowledgeLayer layer))
            {
                errors.Add($"source '{entry.Id}': unknown layer '{entry.Layer}' (expected global|framework|local|skills)");
                continue;
            }
            if (string.IsNullOrWhiteSpace(entry.Root))
            {
                errors.Add($"source '{entry.Id}': 'root' is missing");
                continue;
            }
            if (!Path.IsPathRooted(entry.Root))
            {
                errors.Add($"source '{entry.Id}': root '{entry.Root}' is not an absolute path");
                continue;
            }

            string normalizedRoot = entry.Root.Length > 1 ? entry.Root.TrimEnd('/') : entry.Root;
            bool hasExclude = entry.Exclude is { Count: > 0 };
            if (hasExclude && !normalizedRoot.Contains('*'))
            {
                errors.Add($"source '{entry.Id}': 'exclude' requires a glob root");
                continue;
            }

            List<string> excludePaths = new();
            bool excludePathsValid = true;
            foreach (string excludePath in entry.ExcludePaths ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(excludePath) || Path.IsPathRooted(excludePath) || excludePath.Contains('*'))
                {
                    errors.Add($"source '{entry.Id}': excludePaths entry '{excludePath}' must be a relative path without '*'");
                    excludePathsValid = false;
                    continue;
                }
                excludePaths.Add(excludePath.TrimEnd('/'));
            }
            if (!excludePathsValid)
            {
                continue;
            }

            if (normalizedRoot.Contains('*'))
            {
                string? globError = ExpandGlob(entry.Id, layer, normalizedRoot,
                    entry.Exclude ?? (IReadOnlyCollection<string>)Array.Empty<string>(), excludePaths, sources, seenIds);
                if (globError is not null)
                {
                    errors.Add(globError);
                }
                continue;
            }
            sources.Add(new KnowledgeSource(entry.Id, layer, normalizedRoot) { ExcludePaths = excludePaths });
        }

        Regex? taskPattern = CompileTaskPattern(
            string.IsNullOrWhiteSpace(taskPatternOverride) ? document?.TaskPattern : taskPatternOverride, errors);
        ConstitutionConfig? constitution = ParseConstitution(document?.Constitution, errors);
        SddConfig? sdd = ParseSdd(document?.Sdd, errors);

        if (errors.Count > 0)
        {
            throw new RegistryFormatException("invalid registry: " + string.Join("; ", errors));
        }

        return new KnowledgeRegistry(document!.Machine!, sources, taskPattern, constitution, sdd);
    }

    private static SddConfig? ParseSdd(SddEntry? entry, List<string> errors)
    {
        if (entry is null)
        {
            return null;
        }
        List<string> skills = new();
        foreach (string skill in entry.Skills ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(skill))
            {
                errors.Add("sdd: skills entries must be non-empty skill names");
                continue;
            }
            skills.Add(skill.Trim());
        }
        if (skills.Count == 0)
        {
            errors.Add("sdd: 'skills' is missing or empty — remove the block or list at least one skill name");
            return null;
        }
        return new SddConfig(skills);
    }

    private static ConstitutionConfig? ParseConstitution(ConstitutionEntry? entry, List<string> errors)
    {
        if (entry is null)
        {
            return null;
        }
        bool valid = true;
        if (string.IsNullOrWhiteSpace(entry.VersionFile))
        {
            errors.Add("constitution: 'versionFile' is missing");
            valid = false;
        }
        else if (!Path.IsPathRooted(entry.VersionFile))
        {
            errors.Add($"constitution: versionFile '{entry.VersionFile}' is not an absolute path");
            valid = false;
        }
        if (entry.ScanRoots is null || entry.ScanRoots.Count == 0)
        {
            errors.Add("constitution: 'scanRoots' is missing or empty");
            valid = false;
        }
        List<string> scanRoots = new();
        foreach (string root in entry.ScanRoots ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
            {
                errors.Add($"constitution: scanRoot '{root}' is not an absolute path");
                valid = false;
                continue;
            }
            scanRoots.Add(root.Length > 1 ? root.TrimEnd('/') : root);
        }
        List<string> excludeNames = new();
        foreach (string name in entry.Exclude ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('*'))
            {
                errors.Add($"constitution: exclude entry '{name}' must be a plain directory name");
                valid = false;
                continue;
            }
            excludeNames.Add(name);
        }
        return valid ? new ConstitutionConfig(entry.VersionFile!, scanRoots) { Exclude = excludeNames } : null;
    }

    private static Regex? CompileTaskPattern(string? pattern, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }
        try
        {
            return new Regex(pattern);
        }
        catch (ArgumentException exception)
        {
            errors.Add($"taskPattern '{pattern}' is not a valid regex: {exception.Message}");
            return null;
        }
    }

    private static string? ExpandGlob(string id, KnowledgeLayer layer, string root,
        IReadOnlyCollection<string> exclude, IReadOnlyList<string> excludePaths,
        List<KnowledgeSource> sources, HashSet<string> seenIds)
    {
        string[] segments = root.Split('/');
        if (segments.Any(segment => segment.Contains('*') && segment != "*"))
        {
            return $"source '{id}': root '{root}' — only a whole '*' segment is supported (e.g. /home/u/Repository/*/docs)";
        }

        List<(string Path, List<string> Matched)> candidates = new() { ("/", new List<string>()) };
        foreach (string segment in segments.Where(segment => segment.Length > 0))
        {
            List<(string, List<string>)> next = new();
            foreach ((string path, List<string> matched) in candidates)
            {
                if (segment == "*")
                {
                    foreach (string directory in Directory.Exists(path)
                        ? Directory.EnumerateDirectories(path).Order(StringComparer.Ordinal)
                        : Enumerable.Empty<string>())
                    {
                        next.Add((directory, matched.Append(Path.GetFileName(directory)).ToList()));
                    }
                }
                else
                {
                    next.Add((Path.Combine(path, segment), matched));
                }
            }
            candidates = next;
        }

        foreach ((string path, List<string> matched) in candidates.Where(candidate => Directory.Exists(candidate.Path)))
        {
            if (matched.Any(exclude.Contains))
            {
                continue;
            }
            string expandedId = id + "-" + string.Join("-", matched);
            if (!seenIds.Add(expandedId))
            {
                return $"duplicate source id '{expandedId}' (expanded from glob '{root}')";
            }
            sources.Add(new KnowledgeSource(expandedId, layer, path) { ExcludePaths = excludePaths });
        }
        return null;
    }

    private sealed class RegistryDocument
    {
        public string? Machine { get; set; }
        public string? TaskPattern { get; set; }
        public ConstitutionEntry? Constitution { get; set; }
        public SddEntry? Sdd { get; set; }
        public List<SourceEntry>? Sources { get; set; }
    }

    private sealed class SddEntry
    {
        public List<string>? Skills { get; set; }
    }

    private sealed class ConstitutionEntry
    {
        public string? VersionFile { get; set; }
        public List<string>? ScanRoots { get; set; }
        public List<string>? Exclude { get; set; }
    }

    private sealed class SourceEntry
    {
        public string? Id { get; set; }
        public string? Layer { get; set; }
        public string? Root { get; set; }
        public List<string>? Exclude { get; set; }
        public List<string>? ExcludePaths { get; set; }
    }
}
