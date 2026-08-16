using System.Globalization;
using System.Text;
using Kbo.Bronze;
using Kbo.Jobs;

namespace Kbo.Cli;

/// <summary>
/// Self-check with optional desktop notification (ADR-0016): is the pulse
/// timer armed, and has every known job completed within the dead-man
/// threshold? Installed by kbo init as a login-time service so the owner
/// never has to remember the reboot check.
/// </summary>
public static class DoctorCommand
{
    private const string Usage = "usage: kbo doctor [--notify]";
    private const int CaptureDropThresholdDays = 3;

    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string, string?> environment,
        string homeDirectory,
        IProcessRunner processRunner,
        TimeProvider clock)
    {
        bool notify = false;
        foreach (string argument in args)
        {
            if (argument == "--notify")
            {
                notify = true;
            }
            else
            {
                error.WriteLine(Usage);
                return 1;
            }
        }

        List<string> problems = new();

        ProcessResult timerState = processRunner.Run("systemctl", new[] { "--user", "is-active", "kbo-pulse.timer" });
        string timerStatus = timerState.StandardOutput.Trim();
        output.WriteLine($"timer: {timerStatus}");
        if (timerState.ExitCode != 0)
        {
            problems.Add($"kbo-pulse.timer is {timerStatus} — re-arm with 'kbo init'");
        }

        string eventsRepo = environment(KboEnvironment.EventsRepoVariable)
            ?? KboEnvironment.DefaultEventsRepo(homeDirectory);
        Dictionary<string, DateTimeOffset> lastCompleted = new BronzeStore(eventsRepo).LastCompletedJobs();
        DateTimeOffset now = clock.GetUtcNow();

        if (lastCompleted.Count == 0)
        {
            problems.Add("no job.completed events in bronze — has a pulse ever run?");
        }
        foreach ((string job, DateTimeOffset last) in lastCompleted.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            double daysSilent = (now - last).TotalDays;
            if (daysSilent > JobDeadMan.ThresholdDays(job))
            {
                string line = $"{job}: SILENT {daysSilent.ToString("0.#", CultureInfo.InvariantCulture)}d (last {last:yyyy-MM-dd})";
                output.WriteLine(line);
                problems.Add(line);
            }
            else
            {
                output.WriteLine($"{job}: ok ({daysSilent.ToString("0.#", CultureInfo.InvariantCulture)}d ago)");
            }
        }
        ReportCaptureDrops(homeDirectory, now, output, problems);

        if (problems.Count == 0)
        {
            output.WriteLine("all jobs healthy");
        }

        if (notify)
        {
            SendNotification(processRunner, problems);
        }

        return problems.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Surface capture-error drops (ADR-0029): a fresh drop within the dead-man
    /// threshold is a problem (the registry or hook likely needs a look); a stale
    /// count is informational. The log is never cleared, so the actionable signal
    /// is the last drop's recency, not the running total.
    /// </summary>
    private static void ReportCaptureDrops(
        string homeDirectory, DateTimeOffset now, TextWriter output, List<string> problems)
    {
        string captureLog = KboEnvironment.CaptureErrorLog(homeDirectory);
        if (!File.Exists(captureLog))
        {
            return;
        }

        string[] drops = File.ReadAllLines(captureLog)
            .Where(line => line.Trim().Length > 0)
            .ToArray();
        if (drops.Length == 0)
        {
            return;
        }

        DateTimeOffset? lastDrop = ParseTimestamp(drops[^1]);
        string when = lastDrop is { } value
            ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "unknown";
        string line = $"capture errors: {drops.Length} (last {when})";
        output.WriteLine(line);

        if (lastDrop is { } recent && (now - recent).TotalDays <= CaptureDropThresholdDays)
        {
            problems.Add(line + " — recent capture drops; check the registry/hook");
        }
    }

    private static DateTimeOffset? ParseTimestamp(string logLine)
    {
        string first = logLine.Split('\t', 2)[0];
        return DateTimeOffset.TryParse(
            first, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static void SendNotification(IProcessRunner processRunner, List<string> problems)
    {
        List<string> arguments;
        if (problems.Count == 0)
        {
            arguments = new List<string>
            {
                "--app-name=kbo", "--urgency", "normal",
                "kbo: healthy", "pulse timer armed; all jobs within the dead-man threshold",
            };
        }
        else
        {
            StringBuilder body = new();
            foreach (string problem in problems)
            {
                body.AppendLine(problem);
            }
            arguments = new List<string>
            {
                "--app-name=kbo", "--urgency", "critical",
                $"kbo: {problems.Count} problem(s)", body.ToString().Trim(),
            };
        }
        processRunner.Run("notify-send", arguments);
    }
}
