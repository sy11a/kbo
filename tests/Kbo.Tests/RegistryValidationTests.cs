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
        Assert.Contains("machine", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AbsoluteOrStarredExcludePath_Throws()
    {
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            machine: example-machine
            sources:
              - id: skills-repo
                layer: local
                root: /home/admin/skills-repo
                excludePaths: [/evals]
              - id: other
                layer: local
                root: /home/admin/other
                excludePaths: ['ev*ls']
            """));
        Assert.Contains("'/evals'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'ev*ls'", exception.Message, StringComparison.Ordinal);
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
        Assert.Contains("cosmic", exception.Message, StringComparison.Ordinal);
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
        Assert.Contains("knowledge", exception.Message, StringComparison.Ordinal);
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
        Assert.Contains("Knowledge/notes", exception.Message, StringComparison.Ordinal);
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
        Assert.Contains("root", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_EmptySources_Throws() => _ = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("machine: example-machine"));

    [Fact]
    public void Parse_NotYamlAtAll_Throws() => _ = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("{{{ not yaml"));

    [Fact]
    public void Parse_MetricsArtifact_SurfaceIsCarriedAndDefaultsToNull()
    {
        // per R-001 — optional field: carried when set, unchanged when absent.
        KnowledgeRegistry withField = KnowledgeRegistry.Parse("""
            machine: example-machine
            sources:
              - id: knowledge
                layer: global
                root: /home/admin/Knowledge
                metricsArtifact: /home/admin/Repository/kbl/_generated/graph-metrics.ndjson
              - id: plain
                layer: local
                root: /home/admin/Repository/plain
            """);
        KnowledgeSource knowledge = Assert.Single(withField.Sources, source => source.Id == "knowledge");
        Assert.Equal("/home/admin/Repository/kbl/_generated/graph-metrics.ndjson", knowledge.MetricsArtifact);
        KnowledgeSource plain = Assert.Single(withField.Sources, source => source.Id == "plain");
        Assert.Null(plain.MetricsArtifact);
    }

    [Fact]
    public void Parse_RelativeMetricsArtifact_ThrowsNamingThePath()
    {
        // per R-002 — the pointer must be an absolute file path.
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            machine: example-machine
            sources:
              - id: knowledge
                layer: global
                root: /home/admin/Knowledge
                metricsArtifact: kbl/_generated/graph-metrics.ndjson
            """));
        Assert.Contains("kbl/_generated/graph-metrics.ndjson", exception.Message, StringComparison.Ordinal);
        Assert.Contains("metricsArtifact", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MetricsArtifactOnGlobRoot_Throws()
    {
        // per R-002 — expanded siblings cannot inherit one artifact pointer.
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Parse("""
            machine: example-machine
            sources:
              - id: repo
                layer: local
                root: /home/admin/Repository/*/docs
                metricsArtifact: /home/admin/Repository/kbl/_generated/graph-metrics.ndjson
            """));
        Assert.Contains("glob", exception.Message, StringComparison.Ordinal);
    }
}
