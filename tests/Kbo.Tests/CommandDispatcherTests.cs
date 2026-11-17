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

    [Theory]
    [InlineData("alpha", 10)]
    [InlineData("beta", 11)]
    [InlineData("gamma", 12)]
    public async Task RunAsync_KnownCommand_DispatchesToItsOwnHandlerWithTheRemainingArgsAsync(string name, int expectedExitCode)
    {
        int exitCode = await CommandDispatcher.RunAsync([name, "--flag", "value"], Table("alpha", "beta", "gamma"), _error);

        Assert.Equal(expectedExitCode, exitCode);
        (string calledName, string[] calledArgs) = Assert.Single(_calls);
        Assert.Equal(name, calledName);
        Assert.Equal(["--flag", "value"], calledArgs);
        Assert.Empty(_error.ToString());
    }

    [Fact]
    public async Task RunAsync_CommandWithoutFurtherArgs_PassesAnEmptyArrayAsync()
    {
        int exitCode = await CommandDispatcher.RunAsync(["beta"], Table("alpha", "beta"), _error);

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

        int exitCode = await CommandDispatcher.RunAsync(["only"], table, _error);

        Assert.Equal(handlerExitCode, exitCode);
        Assert.Empty(_error.ToString());
    }

    [Fact]
    public async Task RunAsync_NoArgs_WritesUsageAndFailsAsync()
    {
        int exitCode = await CommandDispatcher.RunAsync([], Table("alpha", "beta"), _error);

        Assert.Equal(1, exitCode);
        Assert.Equal("usage: kbo <alpha | beta> ..." + Environment.NewLine, _error.ToString());
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task RunAsync_UnknownCommand_WritesUsageAndFailsAsync()
    {
        int exitCode = await CommandDispatcher.RunAsync(["delta", "alpha"], Table("alpha", "beta"), _error);

        Assert.Equal(1, exitCode);
        Assert.Equal("usage: kbo <alpha | beta> ..." + Environment.NewLine, _error.ToString());
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task RunAsync_DifferentlyCasedCommand_IsUnknownAsync()
    {
        int exitCode = await CommandDispatcher.RunAsync(["Registry"], Table("registry"), _error);

        Assert.Equal(1, exitCode);
        Assert.Equal("usage: kbo <registry> ..." + Environment.NewLine, _error.ToString());
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task RunAsync_Usage_ListsTheTableNamesInTableOrderAsync()
    {
        int exitCode = await CommandDispatcher.RunAsync(["unknown"], Table("zeta", "alpha", "mu"), _error);

        Assert.Equal(1, exitCode);
        Assert.Equal("usage: kbo <zeta | alpha | mu> ..." + Environment.NewLine, _error.ToString());
    }

    [Fact]
    public async Task RunAsync_ProgramCommandNames_WriteTheExactCliUsageAsync()
    {
        int exitCode = await CommandDispatcher.RunAsync([], Table(_programCommandNames), _error);

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

        int exitCode = await CommandDispatcher.RunAsync(["watch"], table, _error);

        Assert.Equal(7, exitCode);
        Assert.True(continuationRan);
        Assert.Empty(_error.ToString());
    }
}
