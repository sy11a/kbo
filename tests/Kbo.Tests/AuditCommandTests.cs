using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Cli;
using Kbo.Silver;

namespace Kbo.Tests;

public class AuditCommandTests : IDisposable
{
    private readonly string home;
    private readonly string vaultRoot;
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    public AuditCommandTests()
    {
        home = Directory.CreateTempSubdirectory("kbo-audit-cmd-tests").FullName;
        vaultRoot = Path.Combine(home, "Knowledge");
        Directory.CreateDirectory(vaultRoot);
        File.WriteAllText(Path.Combine(home, "registry.yaml"), $"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {vaultRoot}
            """);

        Directory.CreateDirectory(Path.Combine(home, ".claude", "projects", "proj-a"));
        File.WriteAllText(Path.Combine(home, ".claude", "projects", "proj-a", "never-captured.jsonl"), "{}\n");

        string eventsRepo = Path.Combine(home, "Repository", "kb-events");
        new BronzeStore(eventsRepo).Append(new[]
        {
            new JsonObject
            {
                ["id"] = "01E00000000000000000000001",
                ["type"] = "knowledge.read",
                ["time"] = "2026-08-01T10:00:00Z",
                ["subject"] = "/somewhere/unregistered/notes.md",
                ["machine"] = "test-machine",
                ["agent"] = "claude-code",
                ["kbroot"] = null,
                ["data"] = new JsonObject { ["origin"] = "harvest", ["transcript"] = "some-other-file" },
            },
        });
        SilverRebuilder.Rebuild(eventsRepo, Path.Combine(home, ".local", "share", "kbo", "silver.duckdb"));
    }

    public void Dispose()
    {
        Directory.Delete(home, recursive: true);
    }

    [Fact]
    public void Audit_FlagsMissingSessionAndUnregisteredSource_InBothTwins()
    {
        int exitCode = AuditCommand.Run(
            Array.Empty<string>(), output, error,
            name => name == "KBO_REGISTRY" ? Path.Combine(home, "registry.yaml") : null,
            home);

        Assert.Equal(0, exitCode);
        string markdown = File.ReadAllText(Path.Combine(vaultRoot, "_generated", "kbo-audit.md"));
        Assert.Contains("never-captured", markdown);
        Assert.Contains("claude-code", markdown);
        Assert.Contains("kbo harvest", markdown);
        Assert.Contains("/somewhere/unregistered", markdown);
        Assert.DoesNotContain("Not session-auditable", markdown);

        string gold = File.ReadAllText(Path.Combine(vaultRoot, "_generated", "kbo-audit.gold.json"));
        Assert.Contains("\"missingSessions\"", gold);
        Assert.Contains("never-captured", gold);
    }
}
