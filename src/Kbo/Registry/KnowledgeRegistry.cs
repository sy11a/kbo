using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kbo.Registry;

public sealed class KnowledgeRegistry
{
    public string Machine { get; }
    public IReadOnlyList<KnowledgeSource> Sources { get; }

    private KnowledgeRegistry(string machine, IReadOnlyList<KnowledgeSource> sources)
    {
        Machine = machine;
        Sources = sources;
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

    public static KnowledgeRegistry Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new RegistryFormatException($"registry file not found: {path}");
        }

        return Parse(File.ReadAllText(path));
    }

    public static KnowledgeRegistry Parse(string yaml)
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
            if (normalizedRoot.Contains('*'))
            {
                string? globError = ExpandGlob(entry.Id, layer, normalizedRoot, sources, seenIds);
                if (globError is not null)
                {
                    errors.Add(globError);
                }
                continue;
            }
            sources.Add(new KnowledgeSource(entry.Id, layer, normalizedRoot));
        }

        if (errors.Count > 0)
        {
            throw new RegistryFormatException("invalid registry: " + string.Join("; ", errors));
        }

        return new KnowledgeRegistry(document!.Machine!, sources);
    }

    private static string? ExpandGlob(string id, KnowledgeLayer layer, string root, List<KnowledgeSource> sources, HashSet<string> seenIds)
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
            string expandedId = id + "-" + string.Join("-", matched);
            if (!seenIds.Add(expandedId))
            {
                return $"duplicate source id '{expandedId}' (expanded from glob '{root}')";
            }
            sources.Add(new KnowledgeSource(expandedId, layer, path));
        }
        return null;
    }

    private sealed class RegistryDocument
    {
        public string? Machine { get; set; }
        public List<SourceEntry>? Sources { get; set; }
    }

    private sealed class SourceEntry
    {
        public string? Id { get; set; }
        public string? Layer { get; set; }
        public string? Root { get; set; }
    }
}
