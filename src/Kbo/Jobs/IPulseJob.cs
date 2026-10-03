namespace Kbo.Jobs;

public enum JobCadence
{
    Daily,
    Weekly,
}

public interface IPulseJob
{
    public string Name { get; }
    public JobCadence Cadence { get; }

    /// <summary>Runs the job; returns a one-line summary. Throwing means the job failed.</summary>
    public string Run();
}
