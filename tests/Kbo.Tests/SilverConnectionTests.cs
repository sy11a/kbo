using System.Data.Common;
using DuckDB.NET.Data;
using Kbo.Silver;

namespace Kbo.Tests;

public class SilverConnectionTests : IDisposable
{
    private readonly string workspace;
    private readonly string silverPath;

    public SilverConnectionTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-silver-connection-tests").FullName;
        silverPath = Path.Combine(workspace, "silver.duckdb");
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private void CreateSilver()
    {
        using DuckDBConnection connection = new($"Data Source={silverPath}");
        connection.Open();
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE probe AS SELECT 42 AS answer";
        command.ExecuteNonQuery();
    }

    private static long QueryProbe(DuckDBConnection connection)
    {
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = "SELECT answer FROM probe";
        return Convert.ToInt64(
            command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void OpenReadOnly_MissingFile_ThrowsWithRebuildHint()
    {
        FileNotFoundException exception =
            Assert.Throws<FileNotFoundException>(() => SilverConnection.OpenReadOnly(silverPath));
        Assert.Contains("kbo rebuild", exception.Message);
        Assert.Contains(silverPath, exception.Message);
    }

    [Fact]
    public void OpenReadOnly_TwoConcurrentConnections_BothQuery()
    {
        CreateSilver();
        using DuckDBConnection first = SilverConnection.OpenReadOnly(silverPath);
        using DuckDBConnection second = SilverConnection.OpenReadOnly(silverPath);

        Assert.Equal(42, QueryProbe(first));
        Assert.Equal(42, QueryProbe(second));
    }

    [Fact]
    public void OpenReadOnly_RejectsWrites()
    {
        CreateSilver();
        using DuckDBConnection connection = SilverConnection.OpenReadOnly(silverPath);
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE illegal (id INTEGER)";

        Assert.ThrowsAny<DbException>(() => command.ExecuteNonQuery());
    }
}
