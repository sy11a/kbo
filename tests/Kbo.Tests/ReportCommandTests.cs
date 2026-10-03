using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Cli;
using Kbo.Silver;

namespace Kbo.Tests;

public sealed class ReportCommandTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _vaultRoot;
    private readonly string _silverPath;
    private readonly string _registryPath;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public ReportCommandTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-report-tests").FullName;
        _vaultRoot = Path.Combine(_workspace, "Knowledge");
        _silverPath = Path.Combine(_workspace, "silver.duckdb");
        _registryPath = Path.Combine(_workspace, "registry.yaml");
        _ = Directory.CreateDirectory(_vaultRoot);
        File.WriteAllText(Path.Combine(_vaultRoot, "old-note.md"), "# old\n");
        File.SetLastWriteTimeUtc(Path.Combine(_vaultRoot, "old-note.md"), DateTime.UtcNow.AddDays(-200));
        File.WriteAllText(_registryPath, $"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {_vaultRoot}
            """);

        string eventsRepo = Path.Combine(_workspace, "kb-events");
        new BronzeStore(eventsRepo).Append(new[]
        {
            new JsonObject
            {
                ["id"] = "01C00000000000000000000001",
                ["type"] = "knowledge.read",
                ["time"] = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["subject"] = Path.Combine(_vaultRoot, "old-note.md"),
                ["machine"] = "test-machine",
                ["agent"] = "claude-code",
                ["session"] = "sess-1",
                ["kbroot"] = "vault",
                ["data"] = new JsonObject { ["origin"] = "hook" },
            },
        });
        _ = SilverRebuilder.Rebuild(eventsRepo, _silverPath);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_workspace, recursive: true);
    }

    private int Run(params string[] args)
    {
        string? Environment(string name)
        {
            return name switch
            {
                "KBO_REGISTRY" => _registryPath,
                "KBO_SILVER" => _silverPath,
                _ => null,
            };
        }
        return ReportCommand.Run(args, _output, _error, name => Environment(name), _workspace);
    }

    [Fact]
    public void Report_WritesMarkdownGoldTwinAndReadme_IntoVaultGenerated()
    {
        int exitCode = Run();

        Assert.Equal(0, exitCode);
        string generated = Path.Combine(_vaultRoot, "_generated");
        Assert.True(File.Exists(Path.Combine(generated, "kbo-report.md")));
        Assert.True(File.Exists(Path.Combine(generated, "kbo-report.gold.json")));
        Assert.True(File.Exists(Path.Combine(generated, "README.md")));

        string gold = File.ReadAllText(Path.Combine(generated, "kbo-report.gold.json"));
        Assert.Contains("\"machine\": \"test-machine\"", gold, StringComparison.Ordinal);
        Assert.Contains("\"hotNotes\"", gold, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(generated, "kbo-dashboard.html")));
        Assert.True(File.Exists(Path.Combine(generated, "kbo-dashboard.gold.json")));
        Assert.Contains("\"jobHealth\"", File.ReadAllText(Path.Combine(generated, "kbo-dashboard.gold.json")), StringComparison.Ordinal);

        string days = Path.Combine(generated, "days");
        Assert.True(File.Exists(Path.Combine(days, "index.md")));
        Assert.Contains("Daily digests", File.ReadAllText(Path.Combine(days, "index.md")), StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(days, "2026-*.md"));
    }

    [Fact]
    public void Report_MissingSilver_PointsAtRebuild()
    {
        File.Delete(_silverPath);

        int exitCode = Run();

        Assert.Equal(1, exitCode);
        Assert.Contains("kbo rebuild", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Report_RunTwice_Overwrites()
    {
        Assert.Equal(0, Run());
        Assert.Equal(0, Run());
    }
}
