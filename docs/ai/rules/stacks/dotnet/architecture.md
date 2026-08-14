## .NET Architecture Constraints

For .NET how-to guidance (refactoring, testing, EF Core, performance, diagnostics), use the installed `dotnet-*` skills — they cover the doing in depth. This file holds only enforceable constraints; a reviewer must be able to check any diff against every line below:

- The `Domain` project has **zero NuGet dependencies** — enforce this on every build
- Enforce a strict layer reference graph: composition root (Web/Api/Worker) → feature/application slices → Infrastructure → Domain. Never reference the composition root from a library; never reference a feature slice from Infrastructure
- Business logic never touches `HttpContext` directly — always go through a tenant/request-context abstraction (e.g. `ITenantContext`)
- All external AI/LLM provider calls go through a single provider abstraction — never instantiate a provider directly
- Every tenant-scoped entity has a mandatory tenant filter (EF Core global query filter or equivalent) — no tenant-scoped query may bypass it
- Constructor injection only — never resolve services via `IServiceProvider.GetService`, `HttpContext.RequestServices`, or any service-locator pattern outside the composition root (one sanctioned exception: a longer-lived service reaching shorter-lived ones by resolving from a scope it created via `IServiceScopeFactory` — never as a general-purpose locator)
- No static mutable state — state that survives requests breaks multi-tenancy and testability; hold per-request state in scoped services
- No captive dependencies — a singleton service never captures a scoped or transient dependency (including any `DbContext`); take the shorter-lived service per call (method injection, factory, or `IServiceScopeFactory`), never store it in a singleton field
