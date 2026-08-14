## .NET Coding Standards

For C# style guidance beyond these rules, defer to the project's `.editorconfig` and analyzers. This file holds only enforceable constraints; a reviewer must be able to check any diff against every line below:

- Meaningful variable names — never single-letter or abbreviated names (`p`, `e`, `v`)
- Explicit types over `var` for built-in types; editorconfig enforces this where configured
- Braces `{}` on **all** `if` statements, including single-line guard returns
- No alignment-padding around `=` in initializers or assignments
- No comments unless the WHY is non-obvious
- No empty catch blocks; no swallowed exceptions
- No fire-and-forget async — never discard a task (`_ = SomeAsync()`, unawaited `Task.Run`); await it or observe its failure explicitly
- No sync-over-async — never block on async work with `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`
- No `Thread.Sleep` or `Task.Delay` as a synchronization mechanism — synchronize on the actual signal (task, semaphore, event), never on elapsed time
- Every async method that calls a database or external service accepts a `CancellationToken` and forwards it to those calls
- No dead code — unused variables, parameters, methods, using directives, and stubbed method bodies are removed, not kept "just in case" (git history preserves them)
- Treat compiler warnings as errors where the project enables it — zero-warnings policy
