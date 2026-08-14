using Kbo.Jobs;
using Kbo.Registry;

namespace Kbo.Cli;

public static class InitCommand
{
    private const string Usage = "usage: kbo init";
    private static readonly string[] PhaseZeroTimers = ["kb-archive.timer", "kb-backup.timer"];

    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string, string?> environment,
        string homeDirectory,
        IProcessRunner processRunner)
    {
        if (args.Length != 0)
        {
            error.WriteLine(Usage);
            return 1;
        }

        KnowledgeRegistry registry;
        try
        {
            registry = KnowledgeRegistry.Load(
                RegistryLocator.Locate(null, environment, homeDirectory),
                environment(KboEnvironment.TaskPatternVariable));
        }
        catch (RegistryFormatException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
        output.WriteLine($"registry ok: machine '{registry.Machine}', {registry.Sources.Count} source(s)");

        string unitDirectory = Path.Combine(homeDirectory, ".config", "systemd", "user");
        Directory.CreateDirectory(unitDirectory);
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

        Systemctl(processRunner, error, "daemon-reload");
        Systemctl(processRunner, error, "enable", "--now", "kbo-pulse.timer");
        output.WriteLine("kbo-pulse.timer registered and enabled (hourly tick, Persistent=true; bronze decides due-ness)");
        Systemctl(processRunner, error, "enable", "kbo-doctor.service");
        output.WriteLine("kbo-doctor.service enabled (health check + notification at every login)");

        foreach (string timer in PhaseZeroTimers)
        {
            if (File.Exists(Path.Combine(unitDirectory, timer)))
            {
                Systemctl(processRunner, error, "disable", "--now", timer);
                output.WriteLine($"{timer} disabled (unit file kept; re-enable with 'systemctl --user enable --now {timer}')");
            }
        }

        return 0;
    }

    private static void Systemctl(IProcessRunner processRunner, TextWriter error, params string[] arguments)
    {
        List<string> fullArguments = new() { "--user" };
        fullArguments.AddRange(arguments);
        ProcessResult result = processRunner.Run("systemctl", fullArguments);
        if (result.ExitCode != 0)
        {
            error.WriteLine($"systemctl {string.Join(' ', fullArguments)} failed: {result.StandardError.Trim()}");
        }
    }
}
