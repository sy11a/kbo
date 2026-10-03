namespace Kbo.Jobs;

internal interface IProcessRunner
{
    public ProcessResult Run(string fileName, IReadOnlyList<string> arguments);
}
