using Kbo.Cli;
using Kbo.Jobs;

namespace Kbo.Tests;

public class InitCommandTests : IDisposable
{
    private readonly string home;
    private readonly string registryPath;
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    private sealed class FakeRunner : IProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Invocations { get; } = new();

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Invocations.Add((fileName, arguments));
            return new ProcessResult(0, "", "");
        }
    }

    private readonly FakeRunner runner = new();

    public InitCommandTests()
    {
        home = Directory.CreateTempSubdirectory("kbo-init-tests").FullName;
        string vaultRoot = Path.Combine(home, "Knowledge");
        Directory.CreateDirectory(vaultRoot);
        registryPath = Path.Combine(home, "registry.yaml");
        File.WriteAllText(registryPath, $"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {vaultRoot}
            """);
    }

    public void Dispose()
    {
        Directory.Delete(home, recursive: true);
    }

    private int Run()
    {
        return InitCommand.Run(
            Array.Empty<string>(), output, error,
            name => name == "KBO_REGISTRY" ? registryPath : null,
            home, runner);
    }

    [Fact]
    public void Init_WritesTimerAndServiceUnits_AndEnablesTimer()
    {
        int exitCode = Run();

        Assert.Equal(0, exitCode);
        string unitDirectory = Path.Combine(home, ".config", "systemd", "user");
        string service = File.ReadAllText(Path.Combine(unitDirectory, "kbo-pulse.service"));
        string timer = File.ReadAllText(Path.Combine(unitDirectory, "kbo-pulse.timer"));

        Assert.Contains("kbo pulse", service);
        Assert.Contains("Type=oneshot", service);
        Assert.Contains("OnCalendar=hourly", timer);
        Assert.Contains("Persistent=true", timer);

        Assert.Contains(runner.Invocations, i => i.FileName == "systemctl" && i.Arguments.Contains("daemon-reload"));
        Assert.Contains(runner.Invocations,
            i => i.FileName == "systemctl" && i.Arguments.Contains("enable") && i.Arguments.Contains("kbo-pulse.timer"));

        string doctor = File.ReadAllText(Path.Combine(unitDirectory, "kbo-doctor.service"));
        Assert.Contains("kbo doctor --notify", doctor);
        Assert.Contains("WantedBy=default.target", doctor);
        Assert.Contains(runner.Invocations,
            i => i.FileName == "systemctl" && i.Arguments.Contains("enable") && i.Arguments.Contains("kbo-doctor.service"));
    }

    [Fact]
    public void Init_DisablesPhaseZeroTimers_WhenPresent()
    {
        string unitDirectory = Path.Combine(home, ".config", "systemd", "user");
        Directory.CreateDirectory(unitDirectory);
        File.WriteAllText(Path.Combine(unitDirectory, "kb-archive.timer"), "[Timer]");
        File.WriteAllText(Path.Combine(unitDirectory, "kb-backup.timer"), "[Timer]");

        int exitCode = Run();

        Assert.Equal(0, exitCode);
        Assert.Contains(runner.Invocations,
            i => i.FileName == "systemctl" && i.Arguments.Contains("disable") && i.Arguments.Contains("kb-archive.timer"));
        Assert.Contains(runner.Invocations,
            i => i.FileName == "systemctl" && i.Arguments.Contains("disable") && i.Arguments.Contains("kb-backup.timer"));
    }

    [Fact]
    public void Init_NoPhaseZeroTimers_DoesNotTryToDisable()
    {
        Run();

        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("disable"));
    }

    [Fact]
    public void Init_BrokenRegistry_FailsBeforeTouchingSystemd()
    {
        File.WriteAllText(registryPath, "machine: broken");

        int exitCode = Run();

        Assert.Equal(1, exitCode);
        Assert.Empty(runner.Invocations);
        Assert.False(File.Exists(Path.Combine(home, ".config", "systemd", "user", "kbo-pulse.timer")));
    }
}
