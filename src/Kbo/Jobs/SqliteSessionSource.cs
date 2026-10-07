using Microsoft.Data.Sqlite;

namespace Kbo.Jobs;

/// <summary>
/// Session enumeration for agents whose sessions live in a SQLite store.
/// The query must return (id TEXT, modified_ms INTEGER).
/// </summary>
internal sealed record SqliteSessionSource(string DatabasePath, Action<SqliteCommand> SetIdQuery);
