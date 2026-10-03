using System.Data.Common;
using DuckDB.NET.Data;
using Kbo.Silver;

namespace Kbo.Tests;

public sealed class SilverConnectionTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _silverPath;

    public SilverConnectionTests()
    {
        _workspace = Directory.CreateTempSubdirectory("kbo-silver-connection-tests").FullName;
        _silverPath = Path.Combine(_workspace, "silver.duckdb");
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private void CreateSilver()
    {
        using DuckDBConnection connection = new($"Data Source={_silverPath}");
        connection.Open();
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE probe AS SELECT 42 AS answer";
        _ = command.ExecuteNonQuery();
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
            Assert.Throws<FileNotFoundException>(() => SilverConnection.OpenReadOnly(_silverPath));
        Assert.Contains("kbo rebuild", exception.Message, StringComparison.Ordinal);
        Assert.Contains(_silverPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenReadOnly_TwoConcurrentConnections_BothQuery()
    {
        CreateSilver();
        using DuckDBConnection first = SilverConnection.OpenReadOnly(_silverPath);
        using DuckDBConnection second = SilverConnection.OpenReadOnly(_silverPath);

        Assert.Equal(42, QueryProbe(first));
        Assert.Equal(42, QueryProbe(second));
    }

    [Fact]
    public void OpenReadOnly_RejectsWrites()
    {
        CreateSilver();
        using DuckDBConnection connection = SilverConnection.OpenReadOnly(_silverPath);
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE illegal (id INTEGER)";

        _ = Assert.ThrowsAny<DbException>(() => command.ExecuteNonQuery());
    }
}
