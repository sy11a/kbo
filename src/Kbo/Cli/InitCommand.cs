using Kbo.Jobs;
using Kbo.Registry;

namespace Kbo.Cli;

internal static class InitCommand
{
    private const string Usage = "usage: kbo init";
    private static readonly string[] _phaseZeroTimers = ["kb-archive.timer", "kb-backup.timer"];

    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string, string?> environment,
        string homeDirectory,
        IProcessRunner processRunner)
    {
        int? exit = ParseArguments(args, error);
        if (exit is not null)
        {
            return exit.Value;
        }

        KnowledgeRegistry registry;
        try
        {
            registry = KnowledgeRegistry.Load(
                RegistryLocator.Locate(explicitPath: null, environment, homeDirectory),
                environment(KboEnvironment.TaskPatternVariable));
        }
        catch (RegistryFormatException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
        output.WriteLine($"registry ok: machine '{registry.Machine}', {registry.Sources.Count} source(s)");

        string unitDirectory = Path.Combine(homeDirectory, ".config", "systemd", "user");
        _ = Directory.CreateDirectory(unitDirectory);
        WriteUnitFiles(unitDirectory, homeDirectory);

        InstallPulseAndDoctor(processRunner, error, output);
        DisableOldTimers(processRunner, error, output, unitDirectory);

        return 0;
    }

    private static int? ParseArguments(string[] args, TextWriter error)
    {
        if (args.Length is 0)
        {
            return null;
        }
        error.WriteLine(Usage);
        return 1;
    }

    private static void WriteUnitFiles(string unitDirectory, string homeDirectory)
    {
        File.WriteAllText(Path.Combine(unitDirectory, "kbo-pulse.service"), $"""
            [Unit]
            Description=kbo pulse — Practice Observability daily jobs

            [Service]
            Type=oneshot
            ExecStart={Path.Combine(homeDirectory, ".local", "bin", "kbo")} pulse
            """ + "\n");
        File.WriteAllText(Path.Combine(unitDirectory, "kbo-pulse.timer"), """
            [Unit]
            Description=Daily kbo pulse

            [Timer]
            OnCalendar=hourly
            Persistent=true

            [Install]
            WantedBy=timers.target
            """ + "\n");

        File.WriteAllText(Path.Combine(unitDirectory, "kbo-doctor.service"), $"""
            [Unit]
            Description=kbo doctor — health check + desktop notification at login

            [Service]
            Type=oneshot
            ExecStart={Path.Combine(homeDirectory, ".local", "bin", "kbo")} doctor --notify

            [Install]
            WantedBy=default.target
            """ + "\n");
    }

    private static void InstallPulseAndDoctor(IProcessRunner processRunner, TextWriter error, TextWriter output)
    {
        Systemctl(processRunner, error, "daemon-reload");
        Systemctl(processRunner, error, "enable", "--now", "kbo-pulse.timer");
        output.WriteLine("kbo-pulse.timer registered and enabled (hourly tick, Persistent=true; bronze decides due-ness)");
        Systemctl(processRunner, error, "enable", "kbo-doctor.service");
        output.WriteLine("kbo-doctor.service enabled (health check + notification at every login)");
    }

    private static void DisableOldTimers(IProcessRunner processRunner, TextWriter error, TextWriter output, string unitDirectory)
    {
        foreach (string timer in _phaseZeroTimers)
        {
            if (File.Exists(Path.Combine(unitDirectory, timer)))
            {
                Systemctl(processRunner, error, "disable", "--now", timer);
                output.WriteLine($"{timer} disabled (unit file kept; re-enable with 'systemctl --user enable --now {timer}')");
            }
        }
    }

    private static void Systemctl(IProcessRunner processRunner, TextWriter error, params string[] arguments)
    {
        List<string> fullArguments = ["--user", .. arguments];
        ProcessResult result = processRunner.Run("systemctl", fullArguments);
        if (result.ExitCode is 0)
        {
            return;
        }
        error.WriteLine($"systemctl {string.Join(' ', fullArguments)} failed: {result.StandardError.Trim()}");
    }
}