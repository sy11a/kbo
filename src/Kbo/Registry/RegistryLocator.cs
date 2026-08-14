namespace Kbo.Registry;

public static class RegistryLocator
{
    public const string EnvironmentVariable = KboEnvironment.RegistryVariable;

    public static string Locate(string? explicitPath, Func<string, string?> environment, string homeDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        string? fromEnvironment = environment(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        return Path.Combine(homeDirectory, ".config", "kbo", "registry.yaml");
    }
}
