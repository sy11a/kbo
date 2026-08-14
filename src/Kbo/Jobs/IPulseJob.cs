namespace Kbo.Jobs;

public enum JobCadence
{
    Daily,
    Weekly,
}

public interface IPulseJob
{
    string Name { get; }
    JobCadence Cadence { get; }

    /// <summary>Runs the job; returns a one-line summary. Throwing means the job failed.</summary>
    string Run();
}
