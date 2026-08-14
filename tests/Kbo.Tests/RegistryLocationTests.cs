using Kbo.Registry;

namespace Kbo.Tests;

public class RegistryLocationTests
{
    [Fact]
    public void Locate_ExplicitPath_WinsOverEverything()
    {
        string located = RegistryLocator.Locate(
            "/tmp/explicit.yaml",
            _ => "/tmp/from-env.yaml",
            "/home/someone");
        Assert.Equal("/tmp/explicit.yaml", located);
    }

    [Fact]
    public void Locate_NoExplicitPath_UsesEnvironmentVariable()
    {
        string located = RegistryLocator.Locate(
            null,
            name => name == "KBO_REGISTRY" ? "/tmp/from-env.yaml" : null,
            "/home/someone");
        Assert.Equal("/tmp/from-env.yaml", located);
    }

    [Fact]
    public void Locate_NothingSet_DefaultsToXdgConfig()
    {
        string located = RegistryLocator.Locate(null, _ => null, "/home/someone");
        Assert.Equal("/home/someone/.config/kbo/registry.yaml", located);
    }

    [Fact]
    public void Load_ExistingFile_ParsesIt()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
        File.WriteAllText(path, """
            machine: example-machine
            sources:
              - id: knowledge
                layer: global
                root: /home/admin/Knowledge
            """);
        try
        {
            KnowledgeRegistry registry = KnowledgeRegistry.Load(path);
            Assert.Equal("example-machine", registry.Machine);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ThrowsNamingThePath()
    {
        string path = "/nonexistent/kbo/registry.yaml";
        RegistryFormatException exception = Assert.Throws<RegistryFormatException>(() => KnowledgeRegistry.Load(path));
        Assert.Contains(path, exception.Message);
    }
}
