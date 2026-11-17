using Kbo.Cli;
using Kbo.Jobs;

namespace Kbo;

internal static class Program
{
    private const string CaptureCommandName = "capture";

    private static async Task<int> Main(string[] args)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            return await CommandDispatcher.RunAsync(args, Commands(home), Console.Error).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An exception from the command's own thread ends here. Capture keeps its fail-safe contract
            // (ADR-0029): the drop is logged and the exit code stays 0. Every other command reports it and
            // exits 2. The capture AppDomain handler stays for an exception on another thread.
            return args is [CaptureCommandName, string agent, ..]
                ? CaptureCommand.DropUnexpected(home, agent, ex)
                : ReportUnexpected(ex, Console.Error);
        }
    }

    internal static int ReportUnexpected(Exception exception, TextWriter error)
    {
        error.WriteLine($"kbo: internal error: {exception.GetType().FullName}: {exception.Message}");
        error.WriteLine(exception.ToString());
        return 2;
    }

    private static IReadOnlyList<KeyValuePair<string, Func<string[], Task<int>>>> Commands(string home)
    {
        return
        [
            new("registry", args => Task.FromResult(RunRegistry(args, home))),
            new(CaptureCommandName, args => Task.FromResult(RunCapture(args, home))),
            new("harvest", args => Task.FromResult(RunHarvest(args, home))),
            new("rebuild", args => Task.FromResult(RunRebuild(args, home))),
            new("report", args => Task.FromResult(RunReport(args, home))),
            new("audit", args => Task.FromResult(RunAudit(args, home))),
            new("pulse", args => Task.FromResult(RunPulse(args, home))),
            new("init", args => Task.FromResult(RunInit(args, home))),
            new("doctor", args => Task.FromResult(RunDoctor(args, home))),
            new("watch", async args => await RunWatchAsync(args, home).ConfigureAwait(false)),
        ];
    }

    private static int RunRegistry(string[] args, string home) =>
        RegistryCommand.Run(args, Console.Out, Console.Error, key => Environment.GetEnvironmentVariable(key), home);

    private static int RunCapture(string[] args, string home)
    {
        // Last-chance handler for exception types Run's filter doesn't cover.
        // Tests call CaptureCommand.Run in-process and would see this global handler,
        // so the handler is only wired here, the process entry (ADR-0029).
        string agent = args.Length > 0 ? args[0] : string.Empty;
        AppDomain.CurrentDomain.UnhandledException += CaptureCommand.CreateUnhandledExceptionHandler(home, agent);
        return CaptureCommand.Run(args, Console.In, Console.Error, key => Environment.GetEnvironmentVariable(key), home);
    }

    private static int RunHarvest(string[] args, string home) =>
        HarvestCommand.Run(args, Console.Out, Console.Error, key => Environment.GetEnvironmentVariable(key), home);

    private static int RunRebuild(string[] args, string home) =>
        RebuildCommand.Run(args, Console.Out, Console.Error, key => Environment.GetEnvironmentVariable(key), home);

    private static int RunReport(string[] args, string home) =>
        ReportCommand.Run(args, Console.Out, Console.Error, key => Environment.GetEnvironmentVariable(key), home);

    private static int RunAudit(string[] args, string home) =>
        AuditCommand.Run(args, Console.Out, Console.Error, key => Environment.GetEnvironmentVariable(key), home);

    private static int RunPulse(string[] args, string home) =>
        PulseCommand.Run(args, Console.Out, Console.Error, key => Environment.GetEnvironmentVariable(key), home);

    private static int RunInit(string[] args, string home) =>
        InitCommand.Run(args, Console.Out, Console.Error, key => Environment.GetEnvironmentVariable(key), home, new ProcessRunner());

    private static int RunDoctor(string[] args, string home) =>
        DoctorCommand.Run(args, Console.Out, Console.Error, key => Environment.GetEnvironmentVariable(key), home, new ProcessRunner(), TimeProvider.System);

    private static async Task<int> RunWatchAsync(string[] args, string home)
    {
        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        return await WatchCommand.RunAsync(
            args, Console.Out, Console.Error, key => Environment.GetEnvironmentVariable(key), home, cancellation.Token).ConfigureAwait(false);
    }
}
