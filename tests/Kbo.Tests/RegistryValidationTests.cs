using Kbo.Registry;

namespace Kbo.Tests;

public class RegistryValidationTests
{
    [Fact]
    public void Parse_MissingMachine_Throws()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            sources:
              - id: knowledge
                layer: global
                root: /home/admin/Knowledge
            """));
        Assert.Contains("machine", exception.Message);
    }

    [Fact]
    public void Parse_UnknownLayer_ThrowsNamingTheValue()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            machine: example-machine
            sources:
              - id: knowledge
                layer: cosmic
                root: /home/admin/Knowledge
            """));
        Assert.Contains("cosmic", exception.Message);
    }

    [Fact]
    public void Parse_DuplicateId_ThrowsNamingTheId()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            machine: example-machine
            sources:
              - id: knowledge
                layer: global
                root: /home/admin/Knowledge
              - id: knowledge
                layer: skills
                root: /home/admin/.claude/skills
            """));
        Assert.Contains("knowledge", exception.Message);
        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RelativeRoot_ThrowsNamingThePath()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            machine: example-machine
            sources:
              - id: knowledge
                layer: global
                root: Knowledge/notes
            """));
        Assert.Contains("Knowledge/notes", exception.Message);
    }

    [Fact]
    public void Parse_SourceMissingField_Throws()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            machine: example-machine
            sources:
              - id: knowledge
                layer: global
            """));
        Assert.Contains("root", exception.Message);
    }

    [Fact]
    public void Parse_EmptySources_Throws()
    {
        Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("machine: example-machine"));
    }

    [Fact]
    public void Parse_NotYamlAtAll_Throws()
    {
        Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("{{{ not yaml"));
    }
}
