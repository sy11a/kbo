using System.Diagnostics;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Schemas;

namespace Kbo.Jobs;

public static class PulseRunner
{
    public const string AgentName = "kbo";
    private const double WeeklyDueDays = 6.5;

    public static int Run(
        IReadOnlyList<IPulseJob> jobs,
        string eventsRepo,
        string machine,
        TimeProvider clock,
        Random random,
        TextWriter output)
    {
        BronzeStore store = new(eventsRepo);
        Dictionary<string, DateTimeOffset> lastCompleted = store.LastCompletedJobs();
        DateTimeOffset now = clock.GetUtcNow();
        TimeZoneInfo zone = clock.LocalTimeZone;

        int failures = 0;
        foreach (IPulseJob job in jobs)
        {
            if (lastCompleted.TryGetValue(job.Name, out DateTimeOffset last) && !IsDue(job.Cadence, last, now, zone))
            {
                output.WriteLine($"{job.Name}: not due (last completed {last:yyyy-MM-dd HH:mm}Z)");
                continue;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                string summary = job.Run();
                stopwatch.Stop();
                store.Append(new[]
                {
                    JobEvent("job.completed", job.Name, machine, clock, random, new JsonObject
                    {
                        [EventDataFields.Job] = job.Name,
                        [EventDataFields.DurationMs] = stopwatch.ElapsedMilliseconds,
                        ["summary"] = summary,
                    }),
                });
                output.WriteLine($"{job.Name}: completed in {stopwatch.ElapsedMilliseconds}ms — {summary}");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                stopwatch.Stop();
                failures++;
                store.Append(new[]
                {
                    JobEvent("job.failed", job.Name, machine, clock, random, new JsonObject
                    {
                        [EventDataFields.Job] = job.Name,
                        [EventDataFields.DurationMs] = null,
                        [EventDataFields.Error] = exception.Message,
                    }),
                });
                output.WriteLine($"{job.Name}: FAILED — {exception.Message}");
            }
        }

        return failures;
    }

    /// <summary>
    /// Daily jobs are due once per local calendar day; weekly past 6.5 days.
    /// The OS timer ticks hourly — a failed job (no job.completed) stays due
    /// and retries on the next tick; a completed one no-ops until tomorrow.
    /// </summary>
    private static bool IsDue(JobCadence cadence, DateTimeOffset lastCompleted, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (cadence == JobCadence.Weekly)
        {
            return (now - lastCompleted).TotalDays >= WeeklyDueDays;
        }
        return TimeZoneInfo.ConvertTime(lastCompleted, zone).Date < TimeZoneInfo.ConvertTime(now, zone).Date;
    }

    private static JsonObject JobEvent(
        string type, string jobName, string machine, TimeProvider clock, Random random, JsonObject data)
    {
        return EventEnvelope.Create(
            type,
            subject: jobName,
            kbroot: null,
            data,
            machine,
            AgentName,
            session: null,
            repo: null,
            task: null,
            model: null,
            clock.GetUtcNow(),
            random);
    }
}
