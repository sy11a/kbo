using Kbo.Registry;

namespace Kbo.Cli;

public static class RegistryCommand
{
    private const string Usage = "usage: kbo registry <show | resolve <path>> [--registry <file>]";

    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string, string?> environment,
        string homeDirectory)
    {
        List<string> positional = new();
        string? explicitRegistryPath = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--registry")
            {
                if (index + 1 >= args.Length)
                {
                    error.WriteLine("--registry requires a file path");
                    error.WriteLine(Usage);
                    return 1;
                }
                explicitRegistryPath = args[++index];
            }
            else
            {
                positional.Add(args[index]);
            }
        }

        if (positional is not (["show"] or ["resolve", _]))
        {
            error.WriteLine(Usage);
            return 1;
        }

        string registryPath = RegistryLocator.Locate(explicitRegistryPath, environment, homeDirectory);

        KnowledgeRegistry registry;
        try
        {
            registry = KnowledgeRegistry.Load(registryPath);
        }
        catch (RegistryFormatException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }

        if (positional is ["resolve", string path])
        {
            output.WriteLine(registry.Resolve(path) ?? "null");
            return 0;
        }

        output.WriteLine($"machine: {registry.Machine} ({registryPath})");
        foreach (KnowledgeSource source in registry.Sources)
        {
            output.WriteLine($"  {source.Id}  [{source.Layer.ToString().ToLowerInvariant()}]  {source.Root}");
        }
        return 0;
    }
}
