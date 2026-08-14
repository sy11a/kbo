using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Cli;

namespace Kbo.Tests;

public class WatchCommandTests : IDisposable
{
    private readonly string workspace;
    private readonly string vaultRoot;
    private readonly string silverPath;
    private readonly string eventsRepo;
    private readonly string registryPath;
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    public WatchCommandTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-watch-tests").FullName;
        vaultRoot = Path.Combine(workspace, "Knowledge");
        silverPath = Path.Combine(workspace, "silver.duckdb");
        eventsRepo = Path.Combine(workspace, "kb-events");
        registryPath = Path.Combine(workspace, "registry.yaml");
        Directory.CreateDirectory(vaultRoot);
        File.WriteAllText(registryPath, $"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {vaultRoot}
            """);
        new BronzeStore(eventsRepo).Append(new[]
        {
            new JsonObject
            {
                ["id"] = "01E00000000000000000000001",
                ["type"] = "knowledge.read",
                ["time"] = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["subject"] = Path.Combine(vaultRoot, "note.md"),
                ["machine"] = "test-machine",
                ["agent"] = "claude-code",
                ["session"] = "sess-1",
                ["kbroot"] = "vault",
                ["data"] = new JsonObject { ["origin"] = "hook" },
            },
        });
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private Task<int> Run(CancellationToken cancellationToken, params string[] args)
    {
        string? Environment(string name) => name switch
        {
            "KBO_REGISTRY" => registryPath,
            "KBO_SILVER" => silverPath,
            "KBO_EVENTS_REPO" => eventsRepo,
            _ => null,
        };
        return WatchCommand.Run(args, output, error, Environment, workspace, cancellationToken);
    }

    private static CancellationToken Cancelled()
    {
        return new CancellationToken(canceled: true);
    }

    [Fact]
    public async Task Watch_CancelledToken_RendersDashboardOnce_WithAutoReloadAndStops()
    {
        int exitCode = await Run(Cancelled());

        Assert.Equal(0, exitCode);
        string dashboard = Path.Combine(vaultRoot, "_generated", "kbo-dashboard.html");
        Assert.True(File.Exists(dashboard));
        Assert.Contains("http-equiv=\"refresh\" content=\"30\"", File.ReadAllText(dashboard));
        Assert.Contains("kbo watch stopped", output.ToString());
    }

    [Fact]
    public async Task Watch_ExplicitInterval_DrivesTheRefreshContent()
    {
        int exitCode = await Run(Cancelled(), "--interval", "10");

        Assert.Equal(0, exitCode);
        string dashboard = File.ReadAllText(Path.Combine(vaultRoot, "_generated", "kbo-dashboard.html"));
        Assert.Contains("http-equiv=\"refresh\" content=\"10\"", dashboard);
    }

    [Fact]
    public async Task Watch_IntervalBelowMinimum_FailsBeforeRendering()
    {
        int exitCode = await Run(Cancelled(), "--interval", "1");

        Assert.Equal(1, exitCode);
        Assert.Contains("interval", error.ToString());
        Assert.False(File.Exists(Path.Combine(vaultRoot, "_generated", "kbo-dashboard.html")));
    }

    [Fact]
    public async Task Watch_UnknownArgument_ReturnsUsage()
    {
        int exitCode = await Run(Cancelled(), "--bogus");

        Assert.Equal(1, exitCode);
        Assert.Contains("usage: kbo watch", error.ToString());
    }
}
