using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Cli;

namespace Kbo.Tests;

public sealed class WatchCommandTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _vaultRoot;
    private readonly string _silverPath;
    private readonly string _eventsRepo;
    private readonly string _registryPath;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public WatchCommandTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-watch-tests").FullName;
        _vaultRoot = Path.Combine(_workspace, "Knowledge");
        _silverPath = Path.Combine(_workspace, "silver.duckdb");
        _eventsRepo = Path.Combine(_workspace, "kb-events");
        _registryPath = Path.Combine(_workspace, "registry.yaml");
        _ = Directory.CreateDirectory(_vaultRoot);
        File.WriteAllText(_registryPath, $"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {_vaultRoot}
            """);
        new BronzeStore(_eventsRepo).Append(new[]
        {
            new JsonObject
            {
                ["id"] = "01E00000000000000000000001",
                ["type"] = "knowledge.read",
                ["time"] = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["subject"] = Path.Combine(_vaultRoot, "note.md"),
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
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_workspace, recursive: true);
    }

    private async Task<int> RunAsync(CancellationToken cancellationToken, params string[] args)
    {
        string? Environment(string name)
        {
            return name switch
            {
                "KBO_REGISTRY" => _registryPath,
                "KBO_SILVER" => _silverPath,
                "KBO_EVENTS_REPO" => _eventsRepo,
                _ => null,
            };
        }
        return await WatchCommand.RunAsync(args, _output, _error, variable => Environment(variable), _workspace, cancellationToken);
    }

    private static CancellationToken Cancelled() => new(canceled: true);

    [Fact]
    public async Task Watch_CancelledToken_RendersDashboardOnce_WithAutoReloadAndStopsAsync()
    {
        int exitCode = await RunAsync(Cancelled());

        Assert.Equal(0, exitCode);
        string dashboard = Path.Combine(_vaultRoot, "_generated", "kbo-dashboard.html");
        Assert.True(File.Exists(dashboard));
        Assert.Contains("http-equiv=\"refresh\" content=\"30\"", await File.ReadAllTextAsync(dashboard, CancellationToken.None), StringComparison.Ordinal);
        Assert.Contains("kbo watch stopped", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Watch_ExplicitInterval_DrivesTheRefreshContentAsync()
    {
        int exitCode = await RunAsync(Cancelled(), "--interval", "10");

        Assert.Equal(0, exitCode);
        string dashboard = await File.ReadAllTextAsync(Path.Combine(_vaultRoot, "_generated", "kbo-dashboard.html"), CancellationToken.None);
        Assert.Contains("http-equiv=\"refresh\" content=\"10\"", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Watch_IntervalBelowMinimum_FailsBeforeRenderingAsync()
    {
        int exitCode = await RunAsync(Cancelled(), "--interval", "1");

        Assert.Equal(1, exitCode);
        Assert.Contains("interval", _error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_vaultRoot, "_generated", "kbo-dashboard.html")));
    }

    [Fact]
    public async Task Watch_UnknownArgument_ReturnsUsageAsync()
    {
        int exitCode = await RunAsync(Cancelled(), "--bogus");

        Assert.Equal(1, exitCode);
        Assert.Contains("usage: kbo watch", _error.ToString(), StringComparison.Ordinal);
    }
}
