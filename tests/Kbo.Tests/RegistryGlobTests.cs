using Kbo.Registry;

namespace Kbo.Tests;

public sealed class RegistryGlobTests : IDisposable
{
    private readonly string _workspace;

    public RegistryGlobTests() => _workspace = Directory.CreateTempSubdirectory("kbo-registry-glob-tests").FullName;

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    [Fact]
    public void GlobSegment_ExpandsToOneSourcePerMatchingDirectory()
    {
        _ = Directory.CreateDirectory(Path.Combine(_workspace, "RepoA", "docs"));
        _ = Directory.CreateDirectory(Path.Combine(_workspace, "RepoB", "docs"));
        _ = Directory.CreateDirectory(Path.Combine(_workspace, "RepoC"));

        KnowledgeRegistry registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: repo
                layer: local
                root: {_workspace}/*/docs
            """);

        Assert.Equal(2, registry.Sources.Count);
        KnowledgeSource repoA = Assert.Single(registry.Sources, source => source.Id is "repo-RepoA");
        Assert.Equal(Path.Combine(_workspace, "RepoA", "docs"), repoA.Root);
        Assert.Equal(KnowledgeLayer.Local, repoA.Layer);
        Assert.Contains(registry.Sources, source => source.Id is "repo-RepoB");

        Assert.Equal("repo-RepoA", registry.Resolve(Path.Combine(_workspace, "RepoA", "docs", "adr", "0001.md")));
        Assert.Null(registry.Resolve(Path.Combine(_workspace, "RepoC", "readme.md")));
    }

    [Fact]
    public void Glob_NoMatches_YieldsNoSourcesForThatEntry()
    {
        _ = Directory.CreateDirectory(Path.Combine(_workspace, "vault"));

        KnowledgeRegistry registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {_workspace}/vault
              - id: repo
                layer: local
                root: {_workspace}/nothing/*/docs
            """);

        KnowledgeSource only = Assert.Single(registry.Sources);
        Assert.Equal("vault", only.Id);
    }

    [Fact]
    public void Glob_ExpandedIdCollidingWithExplicitId_IsRejected()
    {
        _ = Directory.CreateDirectory(Path.Combine(_workspace, "x", "docs"));
        _ = Directory.CreateDirectory(Path.Combine(_workspace, "explicit"));

        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: repo-x
                layer: local
                root: {_workspace}/explicit
              - id: repo
                layer: local
                root: {_workspace}/*/docs
            """));

        Assert.Contains("duplicate source id 'repo-x'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Glob_ExcludedDirectoryNames_AreSkipped()
    {
        _ = Directory.CreateDirectory(Path.Combine(_workspace, "Alpha", "docs"));
        _ = Directory.CreateDirectory(Path.Combine(_workspace, "Beta", "docs"));
        _ = Directory.CreateDirectory(Path.Combine(_workspace, "kb-observability-private-archive", "docs"));

        KnowledgeRegistry registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: repo
                layer: local
                root: {_workspace}/*/docs
                exclude: [kb-observability-private-archive]
            """);

        string[] ids = [.. registry.Sources.Select(source => source.Id)];
        Assert.Contains("repo-Alpha", ids, StringComparer.Ordinal);
        Assert.Contains("repo-Beta", ids, StringComparer.Ordinal);
        Assert.DoesNotContain("repo-kb-observability-private-archive", ids, StringComparer.Ordinal);
    }

    [Fact]
    public void Exclude_OnNonGlobSource_IsRejected()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() =>
            KnowledgeRegistry.Parse("""
                machine: test-machine
                sources:
                  - id: vault
                    layer: global
                    root: /tmp/vault
                    exclude: [something]
                """));

        Assert.Contains("'exclude' requires a glob root", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Glob_ExcludePaths_PropagateToExpandedSources()
    {
        _ = Directory.CreateDirectory(Path.Combine(_workspace, "Alpha", "docs"));

        KnowledgeRegistry registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: repo
                layer: local
                root: {_workspace}/*/docs
                excludePaths: [ai]
            """);

        KnowledgeSource expanded = Assert.Single(registry.Sources);
        Assert.Equal("repo-Alpha", expanded.Id);
        Assert.Equal(["ai"], expanded.ExcludePaths);
    }

    [Fact]
    public void PartialStarSegment_IsRejected()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: repo
                layer: local
                root: {_workspace}/Repo*/docs
            """));

        Assert.Contains("only a whole '*' segment", exception.Message, StringComparison.Ordinal);
    }
}
