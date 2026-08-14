using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Cli;
using Kbo.Silver;

namespace Kbo.Tests;

public class ReportCommandTests : IDisposable
{
    private readonly string workspace;
    private readonly string vaultRoot;
    private readonly string silverPath;
    private readonly string registryPath;
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    public ReportCommandTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-report-tests").FullName;
        vaultRoot = Path.Combine(workspace, "Knowledge");
        silverPath = Path.Combine(workspace, "silver.duckdb");
        registryPath = Path.Combine(workspace, "registry.yaml");
        Directory.CreateDirectory(vaultRoot);
        File.WriteAllText(Path.Combine(vaultRoot, "old-note.md"), "# old\n");
        File.SetLastWriteTimeUtc(Path.Combine(vaultRoot, "old-note.md"), DateTime.UtcNow.AddDays(-200));
        File.WriteAllText(registryPath, $"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {vaultRoot}
            """);

        string eventsRepo = Path.Combine(workspace, "kb-events");
        new BronzeStore(eventsRepo).Append(new[]
        {
            new JsonObject
            {
                ["id"] = "01C00000000000000000000001",
                ["type"] = "knowledge.read",
                ["time"] = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["subject"] = Path.Combine(vaultRoot, "old-note.md"),
                ["machine"] = "test-machine",
                ["agent"] = "claude-code",
                ["session"] = "sess-1",
                ["kbroot"] = "vault",
                ["data"] = new JsonObject { ["origin"] = "hook" },
            },
        });
        SilverRebuilder.Rebuild(eventsRepo, silverPath);
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private int Run(params string[] args)
    {
        string? Environment(string name) => name switch
        {
            "KBO_REGISTRY" => registryPath,
            "KBO_SILVER" => silverPath,
            _ => null,
        };
        return ReportCommand.Run(args, output, error, Environment, workspace);
    }

    [Fact]
    public void Report_WritesMarkdownGoldTwinAndReadme_IntoVaultGenerated()
    {
        int exitCode = Run();

        Assert.Equal(0, exitCode);
        string generated = Path.Combine(vaultRoot, "_generated");
        Assert.True(File.Exists(Path.Combine(generated, "kbo-report.md")));
        Assert.True(File.Exists(Path.Combine(generated, "kbo-report.gold.json")));
        Assert.True(File.Exists(Path.Combine(generated, "README.md")));

        string gold = File.ReadAllText(Path.Combine(generated, "kbo-report.gold.json"));
        Assert.Contains("\"machine\": \"test-machine\"", gold);
        Assert.Contains("\"hotNotes\"", gold);

        Assert.True(File.Exists(Path.Combine(generated, "kbo-dashboard.html")));
        Assert.True(File.Exists(Path.Combine(generated, "kbo-dashboard.gold.json")));
        Assert.Contains("\"jobHealth\"", File.ReadAllText(Path.Combine(generated, "kbo-dashboard.gold.json")));

        string days = Path.Combine(generated, "days");
        Assert.True(File.Exists(Path.Combine(days, "index.md")));
        Assert.Contains("Daily digests", File.ReadAllText(Path.Combine(days, "index.md")));
        Assert.NotEmpty(Directory.GetFiles(days, "2026-*.md"));
    }

    [Fact]
    public void Report_MissingSilver_PointsAtRebuild()
    {
        File.Delete(silverPath);

        int exitCode = Run();

        Assert.Equal(1, exitCode);
        Assert.Contains("kbo rebuild", error.ToString());
    }

    [Fact]
    public void Report_RunTwice_Overwrites()
    {
        Assert.Equal(0, Run());
        Assert.Equal(0, Run());
    }
}
