using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Cli;
using Kbo.Silver;

namespace Kbo.Tests;

public sealed class AuditCommandTests : IDisposable
{
    private readonly string _home;
    private readonly string _vaultRoot;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public AuditCommandTests()
    {
        _home = Directory.CreateTempSubdirectory("kbo-audit-cmd-tests").FullName;
        _vaultRoot = Path.Combine(_home, "Knowledge");
        _ = Directory.CreateDirectory(_vaultRoot);
        File.WriteAllText(Path.Combine(_home, "registry.yaml"), $"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {_vaultRoot}
            """);

        _ = Directory.CreateDirectory(Path.Combine(_home, ".claude", "projects", "proj-a"));
        File.WriteAllText(Path.Combine(_home, ".claude", "projects", "proj-a", "never-captured.jsonl"), "{}\n");

        string eventsRepo = Path.Combine(_home, "Repository", "kb-events");
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
        _ = SilverRebuilder.Rebuild(eventsRepo, Path.Combine(_home, ".local", "share", "kbo", "silver.duckdb"));
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_home, recursive: true);
    }

    [Fact]
    public void Audit_FlagsMissingSessionAndUnregisteredSource_InBothTwins()
    {
        int exitCode = AuditCommand.Run(
            [], _output, _error,
            name => name is "KBO_REGISTRY" ? Path.Combine(_home, "registry.yaml") : null,
            _home);

        Assert.Equal(0, exitCode);
        string markdown = File.ReadAllText(Path.Combine(_vaultRoot, "_generated", "kbo-audit.md"));
        Assert.Contains("never-captured", markdown, StringComparison.Ordinal);
        Assert.Contains("claude-code", markdown, StringComparison.Ordinal);
        Assert.Contains("kbo harvest", markdown, StringComparison.Ordinal);
        Assert.Contains("/somewhere/unregistered", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Not session-auditable", markdown, StringComparison.Ordinal);

        string gold = File.ReadAllText(Path.Combine(_vaultRoot, "_generated", "kbo-audit.gold.json"));
        Assert.Contains("\"missingSessions\"", gold, StringComparison.Ordinal);
        Assert.Contains("never-captured", gold, StringComparison.Ordinal);
    }
}
