using Kbo.Registry;

namespace Kbo.Tests;

public class RegistryParseTests
{
    private const string SpecExample = """
        machine: example-machine
        sources:
          - id: knowledge
            layer: global
            root: /home/admin/Knowledge
          - id: cc-skills
            layer: skills
            root: /home/admin/.claude/skills
          - id: oc-skills
            layer: skills
            root: /home/admin/.config/opencode/skills
          - id: oc-agents
            layer: skills
            root: /home/admin/.config/opencode/agents
          - id: oc-commands
            layer: skills
            root: /home/admin/.config/opencode/commands
        """;

    [Fact]
    public void Parse_SpecExample_YieldsTypedRegistry()
    {
        KnowledgeRegistry registry = KnowledgeRegistry.Parse(SpecExample);

        Assert.Equal("example-machine", registry.Machine);
        Assert.Equal(5, registry.Sources.Count);

        KnowledgeSource vault = registry.Sources[0];
        Assert.Equal("knowledge", vault.Id);
        Assert.Equal(KnowledgeLayer.Global, vault.Layer);
        Assert.Equal("/home/admin/Knowledge", vault.Root);

        KnowledgeSource commands = registry.Sources[4];
        Assert.Equal("oc-commands", commands.Id);
        Assert.Equal(KnowledgeLayer.Skills, commands.Layer);
    }

    [Fact]
    public void Parse_WithoutTaskPattern_HasNoTaskExtraction()
    {
        KnowledgeRegistry registry = KnowledgeRegistry.Parse(SpecExample);

        Assert.Null(registry.TaskPattern);
    }

    [Fact]
    public void Parse_TaskPattern_CompilesToRegex()
    {
        KnowledgeRegistry registry = KnowledgeRegistry.Parse(
            "machine: m\ntaskPattern: 'JIRA-\\d+'\nsources:\n  - {id: k, layer: global, root: /kb}");

        Assert.NotNull(registry.TaskPattern);
        Assert.Equal("JIRA-42", registry.TaskPattern!.Match("feature/JIRA-42-report").Value);
    }

    [Fact]
    public void Parse_TaskPatternOverride_WinsOverDocument()
    {
        KnowledgeRegistry registry = KnowledgeRegistry.Parse(
            "machine: m\ntaskPattern: 'JIRA-\\d+'\nsources:\n  - {id: k, layer: global, root: /kb}",
            taskPatternOverride: "AC-\\d+");

        Assert.Equal("AC-7", registry.TaskPattern!.Match("feature/AC-7").Value);
    }

    [Fact]
    public void Parse_WhitespaceTaskPatternOverride_FallsBackToDocument()
    {
        KnowledgeRegistry registry = KnowledgeRegistry.Parse(
            "machine: m\ntaskPattern: 'JIRA-\\d+'\nsources:\n  - {id: k, layer: global, root: /kb}",
            taskPatternOverride: "  ");

        Assert.Equal("JIRA-9", registry.TaskPattern!.Match("JIRA-9").Value);
    }

    [Fact]
    public void Parse_InvalidTaskPattern_ThrowsNamingIt()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse(
            "machine: m\ntaskPattern: '('\nsources:\n  - {id: k, layer: global, root: /kb}"));

        Assert.Contains("taskPattern", exception.Message);
    }
}
