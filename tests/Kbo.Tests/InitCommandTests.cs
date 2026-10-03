using Kbo.Cli;
using Kbo.Jobs;

namespace Kbo.Tests;

public sealed class InitCommandTests : IDisposable
{
    private readonly string _home;
    private readonly string _registryPath;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private sealed class FakeRunner : IProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Invocations { get; } = [];

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Invocations.Add((fileName, arguments));
            return new ProcessResult(0, string.Empty, string.Empty);
        }
    }

    private readonly FakeRunner _runner = new();

    public InitCommandTests()
    {
        _home = Directory.CreateTempSubdirectory("kbo-init-tests").FullName;
        string vaultRoot = Path.Combine(_home, "Knowledge");
        _ = Directory.CreateDirectory(vaultRoot);
        _registryPath = Path.Combine(_home, "registry.yaml");
        File.WriteAllText(_registryPath, $"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {vaultRoot}
            """);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_home, recursive: true);
    }

    private int Run()
    {
        return InitCommand.Run(
            [], _output, _error,
            name => name is "KBO_REGISTRY" ? _registryPath : null,
            _home, _runner);
    }

    [Fact]
    public void Init_WritesTimerAndServiceUnits_AndEnablesTimer()
    {
        int exitCode = Run();

        Assert.Equal(0, exitCode);
        string unitDirectory = Path.Combine(_home, ".config", "systemd", "user");
        string service = File.ReadAllText(Path.Combine(unitDirectory, "kbo-pulse.service"));
        string timer = File.ReadAllText(Path.Combine(unitDirectory, "kbo-pulse.timer"));

        Assert.Contains("kbo pulse", service, StringComparison.Ordinal);
        Assert.Contains("Type=oneshot", service, StringComparison.Ordinal);
        Assert.Contains("OnCalendar=hourly", timer, StringComparison.Ordinal);
        Assert.Contains("Persistent=true", timer, StringComparison.Ordinal);

        Assert.Contains(_runner.Invocations, i => i.FileName is "systemctl" && i.Arguments.Contains("daemon-reload", StringComparer.Ordinal));
        Assert.Contains(_runner.Invocations,
            i => i.FileName is "systemctl" && i.Arguments.Contains("enable", StringComparer.Ordinal) && i.Arguments.Contains("kbo-pulse.timer", StringComparer.Ordinal));

        string doctor = File.ReadAllText(Path.Combine(unitDirectory, "kbo-doctor.service"));
        Assert.Contains("kbo doctor --notify", doctor, StringComparison.Ordinal);
        Assert.Contains("WantedBy=default.target", doctor, StringComparison.Ordinal);
        Assert.Contains(_runner.Invocations,
            i => i.FileName is "systemctl" && i.Arguments.Contains("enable", StringComparer.Ordinal) && i.Arguments.Contains("kbo-doctor.service", StringComparer.Ordinal));
    }

    [Fact]
    public void Init_DisablesPhaseZeroTimers_WhenPresent()
    {
        string unitDirectory = Path.Combine(_home, ".config", "systemd", "user");
        _ = Directory.CreateDirectory(unitDirectory);
        File.WriteAllText(Path.Combine(unitDirectory, "kb-archive.timer"), "[Timer]");
        File.WriteAllText(Path.Combine(unitDirectory, "kb-backup.timer"), "[Timer]");

        int exitCode = Run();

        Assert.Equal(0, exitCode);
        Assert.Contains(_runner.Invocations,
            i => i.FileName is "systemctl" && i.Arguments.Contains("disable", StringComparer.Ordinal) && i.Arguments.Contains("kb-archive.timer", StringComparer.Ordinal));
        Assert.Contains(_runner.Invocations,
            i => i.FileName is "systemctl" && i.Arguments.Contains("disable", StringComparer.Ordinal) && i.Arguments.Contains("kb-backup.timer", StringComparer.Ordinal));
    }

    [Fact]
    public void Init_NoPhaseZeroTimers_DoesNotTryToDisable()
    {
        _ = Run();

        Assert.DoesNotContain(_runner.Invocations, i => i.Arguments.Contains("disable", StringComparer.Ordinal));
    }

    [Fact]
    public void Init_BrokenRegistry_FailsBeforeTouchingSystemd()
    {
        File.WriteAllText(_registryPath, "machine: broken");

        int exitCode = Run();

        Assert.Equal(1, exitCode);
        Assert.Empty(_runner.Invocations);
        Assert.False(File.Exists(Path.Combine(_home, ".config", "systemd", "user", "kbo-pulse.timer")));
    }
}
