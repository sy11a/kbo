using Kbo.Registry;

namespace Kbo.Tests;

public class RegistryGlobTests : IDisposable
{
    private readonly string workspace;

    public RegistryGlobTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-registry-glob-tests").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    [Fact]
    public void GlobSegment_ExpandsToOneSourcePerMatchingDirectory()
    {
        Directory.CreateDirectory(Path.Combine(workspace, "RepoA", "docs"));
        Directory.CreateDirectory(Path.Combine(workspace, "RepoB", "docs"));
        Directory.CreateDirectory(Path.Combine(workspace, "RepoC"));

        KnowledgeRegistry registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: repo
                layer: local
                root: {workspace}/*/docs
            """);

        Assert.Equal(2, registry.Sources.Count);
        KnowledgeSource repoA = Assert.Single(registry.Sources, source => source.Id == "repo-RepoA");
        Assert.Equal(Path.Combine(workspace, "RepoA", "docs"), repoA.Root);
        Assert.Equal(KnowledgeLayer.Local, repoA.Layer);
        Assert.Contains(registry.Sources, source => source.Id == "repo-RepoB");

        Assert.Equal("repo-RepoA", registry.Resolve(Path.Combine(workspace, "RepoA", "docs", "adr", "0001.md")));
        Assert.Null(registry.Resolve(Path.Combine(workspace, "RepoC", "readme.md")));
    }

    [Fact]
    public void Glob_NoMatches_YieldsNoSourcesForThatEntry()
    {
        Directory.CreateDirectory(Path.Combine(workspace, "vault"));

        KnowledgeRegistry registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {workspace}/vault
              - id: repo
                layer: local
                root: {workspace}/nothing/*/docs
            """);

        KnowledgeSource only = Assert.Single(registry.Sources);
        Assert.Equal("vault", only.Id);
    }

    [Fact]
    public void Glob_ExpandedIdCollidingWithExplicitId_IsRejected()
    {
        Directory.CreateDirectory(Path.Combine(workspace, "x", "docs"));
        Directory.CreateDirectory(Path.Combine(workspace, "explicit"));

        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: repo-x
                layer: local
                root: {workspace}/explicit
              - id: repo
                layer: local
                root: {workspace}/*/docs
            """));

        Assert.Contains("duplicate source id 'repo-x'", exception.Message);
    }

    [Fact]
    public void Glob_ExcludedDirectoryNames_AreSkipped()
    {
        Directory.CreateDirectory(Path.Combine(workspace, "Alpha", "docs"));
        Directory.CreateDirectory(Path.Combine(workspace, "Beta", "docs"));
        Directory.CreateDirectory(Path.Combine(workspace, "kb-observability-private-archive", "docs"));

        KnowledgeRegistry registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: repo
                layer: local
                root: {workspace}/*/docs
                exclude: [kb-observability-private-archive]
            """);

        string[] ids = registry.Sources.Select(source => source.Id).ToArray();
        Assert.Contains("repo-Alpha", ids);
        Assert.Contains("repo-Beta", ids);
        Assert.DoesNotContain("repo-kb-observability-private-archive", ids);
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

        Assert.Contains("'exclude' requires a glob root", exception.Message);
    }

    [Fact]
    public void Glob_ExcludePaths_PropagateToExpandedSources()
    {
        Directory.CreateDirectory(Path.Combine(workspace, "Alpha", "docs"));

        KnowledgeRegistry registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: repo
                layer: local
                root: {workspace}/*/docs
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
                root: {workspace}/Repo*/docs
            """));

        Assert.Contains("only a whole '*' segment", exception.Message);
    }
}
