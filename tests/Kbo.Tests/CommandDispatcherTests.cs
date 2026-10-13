using Kbo.Cli;

namespace Kbo.Tests;

public sealed class CommandDispatcherTests : IDisposable
{
    private const string ProgramUsage =
        "usage: kbo <registry | capture | harvest | rebuild | report | audit | pulse | init | doctor | watch> ...";

    private static readonly string[] _programCommandNames =
    [
        "registry", "capture", "harvest", "rebuild", "report", "audit", "pulse", "init", "doctor", "watch",
    ];

    private readonly StringWriter _error = new();
    private readonly List<(string Name, string[] Args)> _calls = [];

    public void Dispose() => _error.Dispose();

    private Func<string[], Task<int>> Recording(string name, int exitCode)
    {
        return args =>
        {
            _calls.Add((name, args));
            return Task.FromResult(exitCode);
        };
    }

    private List<KeyValuePair<string, Func<string[], Task<int>>>> Table(params string[] names)
    {
        List<KeyValuePair<string, Func<string[], Task<int>>>> table = [];
        for (int index = 0; index < names.Length; index++)
        {
            table.Add(new(names[index], Recording(names[index], 10 + index)));
        }

        return table;
    }

    private async Task<int> RunAsync(IReadOnlyList<KeyValuePair<string, Func<string[], Task<int>>>> table, params string[] args) =>
        await CommandDispatcher.RunAsync(args, table, _error);

    [Theory]
    [InlineData("alpha", 10)]
    [InlineData("beta", 11)]
    [InlineData("gamma", 12)]
    public async Task RunAsync_KnownCommand_DispatchesToItsOwnHandlerWithTheRemainingArgsAsync(string name, int expectedExitCode)
    {
        int exitCode = await RunAsync(Table("alpha", "beta", "gamma"), name, "--flag", "value");

        Assert.Equal(expectedExitCode, exitCode);
        (string calledName, string[] calledArgs) = Assert.Single(_calls);
        Assert.Equal(name, calledName);
        Assert.Equal(["--flag", "value"], calledArgs);
        Assert.Empty(_error.ToString());
    }

    [Fact]
    public async Task RunAsync_CommandWithoutFurtherArgs_PassesAnEmptyArrayAsync()
    {
        int exitCode = await RunAsync(Table("alpha", "beta"), "beta");

        Assert.Equal(11, exitCode);
        (string calledName, string[] calledArgs) = Assert.Single(_calls);
        Assert.Equal("beta", calledName);
        Assert.Empty(calledArgs);
        Assert.Empty(_error.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    public async Task RunAsync_KnownCommand_ReturnsTheHandlersExitCodeAsync(int handlerExitCode)
    {
        List<KeyValuePair<string, Func<string[], Task<int>>>> table = [new("only", Recording("only", handlerExitCode))];

        int exitCode = await RunAsync(table, "only");

        Assert.Equal(handlerExitCode, exitCode);
        Assert.Empty(_error.ToString());
    }

    [Fact]
    public async Task RunAsync_NoArgs_WritesUsageAndFailsAsync()
    {
        int exitCode = await RunAsync(Table("alpha", "beta"));

        Assert.Equal(1, exitCode);
        Assert.Equal("usage: kbo <alpha | beta> ..." + Environment.NewLine, _error.ToString());
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task RunAsync_UnknownCommand_WritesUsageAndFailsAsync()
    {
        int exitCode = await RunAsync(Table("alpha", "beta"), "delta", "alpha");

        Assert.Equal(1, exitCode);
        Assert.Equal("usage: kbo <alpha | beta> ..." + Environment.NewLine, _error.ToString());
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task RunAsync_DifferentlyCasedCommand_IsUnknownAsync()
    {
        int exitCode = await RunAsync(Table("registry"), "Registry");

        Assert.Equal(1, exitCode);
        Assert.Equal("usage: kbo <registry> ..." + Environment.NewLine, _error.ToString());
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task RunAsync_Usage_ListsTheTableNamesInTableOrderAsync()
    {
        int exitCode = await RunAsync(Table("zeta", "alpha", "mu"), "unknown");

        Assert.Equal(1, exitCode);
        Assert.Equal("usage: kbo <zeta | alpha | mu> ..." + Environment.NewLine, _error.ToString());
    }

    [Fact]
    public async Task RunAsync_ProgramCommandNames_WriteTheExactCliUsageAsync()
    {
        int exitCode = await RunAsync(Table(_programCommandNames));

        Assert.Equal(1, exitCode);
        Assert.Equal(ProgramUsage + Environment.NewLine, _error.ToString());
    }

    [Fact]
    public async Task RunAsync_AsyncHandler_AwaitsItsResultAsync()
    {
        bool continuationRan = false;
        List<KeyValuePair<string, Func<string[], Task<int>>>> table =
        [
            new("watch", async _ =>
            {
                await Task.Yield();
                continuationRan = true;
                return 7;
            }),
        ];

        int exitCode = await RunAsync(table, "watch");

        Assert.Equal(7, exitCode);
        Assert.True(continuationRan);
        Assert.Empty(_error.ToString());
    }
}
