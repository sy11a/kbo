using Kbo.Cli;

namespace Kbo;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        switch (args)
        {
            case ["registry", ..]:
                return RegistryCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error,
                    key => Environment.GetEnvironmentVariable(key),
                    home);
            case ["capture", ..]:
                return CaptureCommand.Run(
                    args[1..],
                    Console.In,
                    Console.Out,
                    Console.Error,
                    key => Environment.GetEnvironmentVariable(key),
                    home);
            case ["harvest", ..]:
                return HarvestCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error,
                    key => Environment.GetEnvironmentVariable(key),
                    home);
            case ["rebuild", ..]:
                return RebuildCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error,
                    key => Environment.GetEnvironmentVariable(key),
                    home);
            case ["report", ..]:
                return ReportCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error,
                    key => Environment.GetEnvironmentVariable(key),
                    home);
            case ["audit", ..]:
                return AuditCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error,
                    key => Environment.GetEnvironmentVariable(key),
                    home);
            case ["pulse", ..]:
                return PulseCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error,
                    key => Environment.GetEnvironmentVariable(key),
                    home);
            case ["init", ..]:
                return InitCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error,
                    key => Environment.GetEnvironmentVariable(key),
                    home,
                    new Kbo.Jobs.ProcessRunner());
            case ["doctor", ..]:
                return DoctorCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error,
                    key => Environment.GetEnvironmentVariable(key),
                    home,
                    new Kbo.Jobs.ProcessRunner(),
                    TimeProvider.System);
            case ["watch", ..]:
                {
                    using CancellationTokenSource cancellation = new();
                    Console.CancelKeyPress += (_, eventArgs) =>
                    {
                        eventArgs.Cancel = true;
                        cancellation.Cancel();
                    };
                    return await WatchCommand.Run(
                        args[1..],
                        Console.Out,
                        Console.Error,
                        key => Environment.GetEnvironmentVariable(key),
                        home,
                        cancellation.Token);
                }
            default:
                {
                    await Console.Error.WriteLineAsync("usage: kbo <registry | capture | harvest | rebuild | report | audit | pulse | init | doctor | watch> ...");
                    return 1;
                }
        }
    }
}
