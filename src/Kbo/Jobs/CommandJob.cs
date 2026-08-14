namespace Kbo.Jobs;

/// <summary>
/// Wraps a kbo CLI command as a pulse job: nonzero exit becomes a job failure
/// carrying the command's error output.
/// </summary>
public sealed class CommandJob : IPulseJob
{
    private readonly Func<TextWriter, TextWriter, int> command;

    public CommandJob(string name, JobCadence cadence, Func<TextWriter, TextWriter, int> command)
    {
        Name = name;
        Cadence = cadence;
        this.command = command;
    }

    public string Name { get; }
    public JobCadence Cadence { get; }

    public string Run()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        int exitCode = command(output, error);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"exit {exitCode}: {error.ToString().Trim()}");
        }

        string[] lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[^1].Trim() : "ok";
    }
}
