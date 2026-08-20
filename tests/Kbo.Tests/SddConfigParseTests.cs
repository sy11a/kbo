using Kbo.Registry;

namespace Kbo.Tests;

public class SddConfigParseTests
{
    [Fact]
    public void Parse_SddBlock_YieldsSkillSet()
    {
        KnowledgeRegistry registry = KnowledgeRegistry.Parse("""
            machine: example-machine
            sdd:
              skills:
                - legislator
                - superpowers:brainstorm
            sources:
              - id: knowledge
                layer: global
                root: /home/u/Knowledge
            """);

        Assert.NotNull(registry.Sdd);
        Assert.Equal(["legislator", "superpowers:brainstorm"], registry.Sdd.Skills);
    }

    [Fact]
    public void Parse_AbsentSddBlock_LeavesMetricUnconfigured()
    {
        KnowledgeRegistry registry = KnowledgeRegistry.Parse("""
            machine: example-machine
            sources:
              - id: knowledge
                layer: global
                root: /home/u/Knowledge
            """);

        Assert.Null(registry.Sdd);
    }

    [Fact]
    public void Parse_EmptySkillsList_IsRejected()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            machine: example-machine
            sdd:
              skills: []
            sources:
              - id: knowledge
                layer: global
                root: /home/u/Knowledge
            """));

        Assert.Contains("sdd: 'skills' is missing or empty", exception.Message);
    }

    [Fact]
    public void Parse_BlankSkillName_IsRejected()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            machine: example-machine
            sdd:
              skills:
                - "  "
            sources:
              - id: knowledge
                layer: global
                root: /home/u/Knowledge
            """));

        Assert.Contains("sdd: skills entries must be non-empty", exception.Message);
    }
}
