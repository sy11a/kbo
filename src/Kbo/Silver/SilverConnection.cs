using DuckDB.NET.Data;

namespace Kbo.Silver;

/// <summary>
/// How gold readers open silver: read-only, so concurrent readers (watch's
/// dashboard compute, pulse's weekly report/audit) share the file instead of
/// taking exclusive locks (ADR-0032). Writing goes through SilverRebuilder only.
/// </summary>
public static class SilverConnection
{
    public static DuckDBConnection OpenReadOnly(string silverPath)
    {
        if (!File.Exists(silverPath))
        {
            throw new FileNotFoundException(
                $"silver not found at {silverPath} — run 'kbo rebuild' first", silverPath);
        }
        DuckDBConnection connection = new($"Data Source={silverPath};ACCESS_MODE=READ_ONLY");
        connection.Open();
        return connection;
    }
}
