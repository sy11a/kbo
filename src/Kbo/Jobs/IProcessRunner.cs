namespace Kbo.Jobs;

internal interface IProcessRunner
{
    ProcessResult Run(string fileName, IReadOnlyList<string> arguments);
}
