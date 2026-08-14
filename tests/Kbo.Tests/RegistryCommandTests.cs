using Kbo.Cli;

namespace Kbo.Tests;

public class RegistryCommandTests : IDisposable
{
    private readonly string registryPath;
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    public RegistryCommandTests()
    {
        registryPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
        File.WriteAllText(registryPath, """
            machine: example-machine
            sources:
              - id: knowledge
                layer: global
                root: /home/admin/Knowledge
              - id: cc-skills
                layer: skills
                root: /home/admin/.claude/skills
            """);
    }

    public void Dispose()
    {
        File.Delete(registryPath);
    }

    private int Run(params string[] args)
    {
        return RegistryCommand.Run(args, output, error, _ => null, "/home/nobody");
    }

    [Fact]
    public void Show_PrintsMachineAndSources()
    {
        int exitCode = Run("show", "--registry", registryPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("example-machine", output.ToString());
        Assert.Contains("knowledge", output.ToString());
        Assert.Contains("/home/admin/.claude/skills", output.ToString());
    }

    [Fact]
    public void Resolve_PathUnderRoot_PrintsSourceId()
    {
        int exitCode = Run("resolve", "/home/admin/Knowledge/rituals/note.md", "--registry", registryPath);

        Assert.Equal(0, exitCode);
        Assert.Equal("knowledge", output.ToString().Trim());
    }

    [Fact]
    public void Resolve_UnregisteredPath_PrintsNull()
    {
        int exitCode = Run("resolve", "/home/admin/Downloads/x.md", "--registry", registryPath);

        Assert.Equal(0, exitCode);
        Assert.Equal("null", output.ToString().Trim());
    }

    [Fact]
    public void MissingRegistryFile_ReportsErrorAndFails()
    {
        int exitCode = Run("show", "--registry", "/nonexistent/registry.yaml");

        Assert.Equal(1, exitCode);
        Assert.Contains("/nonexistent/registry.yaml", error.ToString());
    }

    [Fact]
    public void UnknownSubcommand_ReportsUsageAndFails()
    {
        int exitCode = Run("frobnicate");

        Assert.Equal(1, exitCode);
        Assert.Contains("usage", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnvironmentVariable_LocatesRegistry()
    {
        int exitCode = RegistryCommand.Run(
            new[] { "resolve", "/home/admin/Knowledge/a.md" },
            output,
            error,
            name => name == "KBO_REGISTRY" ? registryPath : null,
            "/home/nobody");

        Assert.Equal(0, exitCode);
        Assert.Equal("knowledge", output.ToString().Trim());
    }
}
