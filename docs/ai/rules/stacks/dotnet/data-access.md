## .NET Data-Access Constraints

For EF Core how-to guidance (query optimization, migrations, diagnostics), use the installed `dotnet-*` skills. This file holds only enforceable constraints; a reviewer must be able to check any diff against every line below:

- No N+1 query patterns — related entities are loaded eagerly (`Include`) or in a single batched second query, never one query per loop iteration
- `AsNoTracking()` on read-only queries — tracking is reserved for entities the operation actually mutates
- No unbounded result sets — every query returning a list is paginated or capped (`Take(n)`); no bare `ToListAsync()` on an unbounded table
- No `SaveChangesAsync()` inside a loop — batch the changes and save once, unless per-item transactional isolation is the documented intent
- Raw SQL only through parameterized forms — `FromSqlRaw`/`ExecuteSqlRaw` with placeholders or `FormattableString` interpolation; never string-concatenated or manually interpolated SQL
- Deterministic disposal — anything holding a connection, transaction, or other unmanaged resource implements or is wrapped in `IDisposable`/`IAsyncDisposable` and is disposed via `using`/`await using`
