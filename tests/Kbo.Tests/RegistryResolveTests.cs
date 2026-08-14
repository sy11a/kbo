using Kbo.Registry;

namespace Kbo.Tests;

public class RegistryResolveTests
{
    private static KnowledgeRegistry BuildRegistry()
    {
        return KnowledgeRegistry.Parse("""
            machine: example-machine
            sources:
              - id: knowledge
                layer: global
                root: /home/user/Knowledge
              - id: sample-app
                layer: local
                root: /home/user/Repository/SampleApp
              - id: sample-app-docs
                layer: framework
                root: /home/user/Repository/SampleApp/docs/framework
            """);
    }

    [Fact]
    public void Resolve_PathUnderRoot_ReturnsSourceId()
    {
        Assert.Equal("knowledge", BuildRegistry().Resolve("/home/user/Knowledge/rituals/2026-08-11.md"));
    }

    [Fact]
    public void Resolve_PathOutsideAllRoots_ReturnsNull()
    {
        Assert.Null(BuildRegistry().Resolve("/home/user/Downloads/notes.md"));
    }

    [Fact]
    public void Resolve_RootItself_ReturnsSourceId()
    {
        Assert.Equal("knowledge", BuildRegistry().Resolve("/home/user/Knowledge"));
    }

    [Fact]
    public void Resolve_SiblingWithRootAsPrefix_ReturnsNull()
    {
        Assert.Null(BuildRegistry().Resolve("/home/user/KnowledgeBackup/old.md"));
    }

    [Fact]
    public void Resolve_NestedRoots_LongestRootWins()
    {
        Assert.Equal(
            "sample-app-docs",
            BuildRegistry().Resolve("/home/user/Repository/SampleApp/docs/framework/api.md"));
        Assert.Equal(
            "sample-app",
            BuildRegistry().Resolve("/home/user/Repository/SampleApp/src/Program.cs"));
    }

    [Fact]
    public void Resolve_TrailingSlashOnPath_StillResolves()
    {
        Assert.Equal("knowledge", BuildRegistry().Resolve("/home/user/Knowledge/"));
    }
}
