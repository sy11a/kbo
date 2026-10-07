using Kbo.Cli;
using Kbo.Jobs;

namespace Kbo;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        switch (args)
        {
            case ["registry", ..]: return RunRegistry(args[1..], home);
            case ["capture", ..]: return RunCapture(args[1..], home);
            case ["harvest", ..]: return RunHarvest(args[1..], home);
            case ["rebuild", ..]: return RunRebuild(args[1..], home);
            case ["report", ..]: return RunReport(args[1..], home);
            case ["audit", ..]: return RunAudit(args[1..], home);
            case ["pulse", ..]: return RunPulse(args[1..], home);
            case ["init", ..]: return RunInit(args[1..], home);
            case ["doctor", ..]: return RunDoctor(args[1..], home);
            case ["watch", ..]: return await RunWatchAsync(args[1..], home).ConfigureAwait(false);
            default:
                {
                    await Console.Error.WriteLineAsync("usage: kbo <registry | capture | harvest | rebuild | report | audit | pulse | init | doctor | watch> ...").ConfigureAwait(false);
                    return 1;
                }
        }
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
