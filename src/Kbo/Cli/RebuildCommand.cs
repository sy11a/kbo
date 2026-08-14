using Kbo.Silver;

namespace Kbo.Cli;

public static class RebuildCommand
{
    private const string Usage = "usage: kbo rebuild [--silver <file>] [--events-repo <dir>]";

    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string, string?> environment,
        string homeDirectory)
    {
        string? explicitSilver = null;
        string? explicitEventsRepo = null;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--silver" when index + 1 < args.Length:
                    explicitSilver = args[++index];
                    break;
                case "--events-repo" when index + 1 < args.Length:
                    explicitEventsRepo = args[++index];
                    break;
                default:
                    error.WriteLine(Usage);
                    return 1;
            }
        }

        string eventsRepo = explicitEventsRepo
            ?? environment(KboEnvironment.EventsRepoVariable)
            ?? KboEnvironment.DefaultEventsRepo(homeDirectory);
        if (!Directory.Exists(eventsRepo))
        {
            error.WriteLine($"events repo not found: {eventsRepo}");
            return 1;
        }

        string silverPath = explicitSilver
            ?? environment(KboEnvironment.SilverVariable)
            ?? KboEnvironment.DefaultSilverPath(homeDirectory);

        RebuildResult result = SilverRebuilder.Rebuild(eventsRepo, silverPath);
        output.WriteLine(
            $"rebuilt {silverPath}: {result.EventCount} event(s), {result.SessionCount} session(s); {result.SkippedLines} unparseable line(s) skipped");
        return 0;
    }
}
