using Kbo.Registry;

namespace Kbo.Tests;

public class ConstitutionConfigParseTests
{
    private const string WithConstitution = """
        machine: example-machine
        constitution:
          versionFile: /home/u/legislator/skill/VERSION
          scanRoots:
            - /home/u/Repository
            - /home/u/Agent/
        sources:
          - id: knowledge
            layer: global
            root: /home/u/Knowledge
        """;

    [Fact]
    public void Parse_ConstitutionBlock_YieldsConfig()
    {
        KnowledgeRegistry registry = KnowledgeRegistry.Parse(WithConstitution);

        Assert.NotNull(registry.Constitution);
        Assert.Equal("/home/u/legislator/skill/VERSION", registry.Constitution.VersionFile);
        Assert.Equal(["/home/u/Repository", "/home/u/Agent"], registry.Constitution.ScanRoots);
    }

    [Fact]
    public void Parse_ExcludeNames_AreCarried()
    {
        KnowledgeRegistry registry = KnowledgeRegistry.Parse("""
            machine: example-machine
            constitution:
              versionFile: /home/u/legislator/skill/VERSION
              scanRoots:
                - /home/u/Repository
              exclude:
                - some-archived-repo
            sources:
              - id: knowledge
                layer: global
                root: /home/u/Knowledge
            """);

        Assert.Equal(["some-archived-repo"], registry.Constitution!.Exclude);
    }

    [Fact]
    public void Parse_ExcludeWithPathOrGlob_IsRejected()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            machine: example-machine
            constitution:
              versionFile: /home/u/legislator/skill/VERSION
              scanRoots:
                - /home/u/Repository
              exclude:
                - nested/path
            sources:
              - id: knowledge
                layer: global
                root: /home/u/Knowledge
            """));

        Assert.Contains("exclude", exception.Message);
        Assert.Contains("plain directory name", exception.Message);
    }

    [Fact]
    public void Parse_WithoutConstitutionBlock_IsNull()
    {
        KnowledgeRegistry registry = KnowledgeRegistry.Parse("""
            machine: example-machine
            sources:
              - id: knowledge
                layer: global
                root: /home/u/Knowledge
            """);

        Assert.Null(registry.Constitution);
    }

    [Fact]
    public void Parse_RelativeVersionFile_IsRejected()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            machine: example-machine
            constitution:
              versionFile: legislator/skill/VERSION
              scanRoots:
                - /home/u/Repository
            sources:
              - id: knowledge
                layer: global
                root: /home/u/Knowledge
            """));

        Assert.Contains("versionFile", exception.Message);
        Assert.Contains("absolute", exception.Message);
    }

    [Fact]
    public void Parse_MissingScanRoots_IsRejected()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            machine: example-machine
            constitution:
              versionFile: /home/u/legislator/skill/VERSION
            sources:
              - id: knowledge
                layer: global
                root: /home/u/Knowledge
            """));

        Assert.Contains("scanRoots", exception.Message);
    }

    [Fact]
    public void Parse_RelativeScanRoot_IsRejected()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            machine: example-machine
            constitution:
              versionFile: /home/u/legislator/skill/VERSION
              scanRoots:
                - Repository
            sources:
              - id: knowledge
                layer: global
                root: /home/u/Knowledge
            """));

        Assert.Contains("scanRoot", exception.Message);
        Assert.Contains("absolute", exception.Message);
    }
}
