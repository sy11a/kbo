using Kbo.Cli;

namespace Kbo.Tests;

public sealed class RegistryCommandTests : IDisposable
{
    private readonly string _registryPath;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public RegistryCommandTests()
    {
        _registryPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
        File.WriteAllText(_registryPath, """
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
        _output.Dispose();
        _error.Dispose();
        File.Delete(_registryPath);
    }

    private int Run(params string[] args) => RegistryCommand.Run(args, _output, _error, _ => null, "/home/nobody");

    [Fact]
    public void Show_PrintsMachineAndSources()
    {
        int exitCode = Run("show", "--registry", _registryPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("example-machine", _output.ToString(), StringComparison.Ordinal);
        Assert.Contains("knowledge", _output.ToString(), StringComparison.Ordinal);
        Assert.Contains("/home/admin/.claude/skills", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_PathUnderRoot_PrintsSourceId()
    {
        int exitCode = Run("resolve", "/home/admin/Knowledge/rituals/note.md", "--registry", _registryPath);

        Assert.Equal(0, exitCode);
        Assert.Equal("knowledge", _output.ToString().Trim());
    }

    [Fact]
    public void Resolve_UnregisteredPath_PrintsNull()
    {
        int exitCode = Run("resolve", "/home/admin/Downloads/x.md", "--registry", _registryPath);

        Assert.Equal(0, exitCode);
        Assert.Equal("null", _output.ToString().Trim());
    }

    [Fact]
    public void MissingRegistryFile_ReportsErrorAndFails()
    {
        int exitCode = Run("show", "--registry", "/nonexistent/registry.yaml");

        Assert.Equal(1, exitCode);
        Assert.Contains("/nonexistent/registry.yaml", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownSubcommand_ReportsUsageAndFails()
    {
        int exitCode = Run("frobnicate");

        Assert.Equal(1, exitCode);
        Assert.Contains("usage", _error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnvironmentVariable_LocatesRegistry()
    {
        int exitCode = RegistryCommand.Run(
            ["resolve", "/home/admin/Knowledge/a.md"],
            _output,
            _error,
            name => name is "KBO_REGISTRY" ? _registryPath : null,
            "/home/nobody");

        Assert.Equal(0, exitCode);
        Assert.Equal("knowledge", _output.ToString().Trim());
    }
}
