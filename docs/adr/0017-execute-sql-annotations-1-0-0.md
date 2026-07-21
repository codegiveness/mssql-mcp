# execute_sql annotations vary by access mode (programmatic registration)

Switch from static `WithToolsFromAssembly` registration to programmatic `McpServerTool.Create` calls in `Program.cs` so `execute_sql` advertises correct per-mode annotations. **Supersedes the Addendum of ADR-0001** (which assumed the SDK only supported static, compile-time annotations and accepted `destructiveHint=False` in Unrestricted mode as a known limitation). ADR-0001's body stays intact.

## Context

ADR-0001 ships Restricted (default) and Unrestricted modes. The `[McpServerTool(ReadOnly = true, Destructive = false)]` attribute on `ExecuteSql` is a compile-time constant, but `execute_sql` in Unrestricted mode runs DML/DDL that commits immediately — it is neither read-only nor non-destructive. `WithToolsFromAssembly` reads the attribute and cannot vary it by runtime `--access-mode`. The SDK 1.4.1 `McpServerTool.Create(MethodInfo, Func<RequestContext<CallToolRequestParams>, object>, McpServerToolCreateOptions?)` overload accepts `bool?` `ReadOnly`/`Destructive`/`Idempotent`/`OpenWorld` on `McpServerToolCreateOptions`; `DeriveOptions` uses `??=` so passed-in options override attribute values, flowing into `ProtocolTool.Annotations` (`ReadOnlyHint`/`DestructiveHint`). This is the runtime annotation path ADR-0001's Addendum said would be revisited if the SDK added it.

## Decision

Register all 9 tools via `McpServerTool.Create` with the DI `createTargetFunc` overload (resolves the tool class from `IServiceProvider` per invocation via `ActivatorUtilities.CreateInstance`, matching the SDK's internal `WithToolsFromAssembly` behavior). `execute_sql` annotations branch on `options.AccessMode`: `ReadOnly=false, Destructive=true` in Unrestricted mode; `ReadOnly=true, Destructive=false` in Restricted mode. The other 8 tools advertise `ReadOnly=true, Destructive=false` in both modes (unchanged). The `[McpServerTool]` attributes on the tool classes stay as documentation of intent but are overridden at registration time.

## Consequences

- Agents receive an accurate `destructiveHint=true` on `execute_sql` in Unrestricted mode, enabling pre-confirmation prompts for destructive operations (closes the ADR-0001 limitation).
- ADR-0001's Addendum is superseded; its body (the dual-mode decision) stands.
- The 9 `McpServerTool.Create` calls are inlined in `Program.cs` (no new abstraction, per CLAUDE.md §2).
- Bumping the SDK past 1.4.1 requires re-verifying the `McpServerToolCreateOptions` shape (the `bool?` properties and the DI `createTargetFunc` overload).

## Status

Accepted (2026-07-21). Supersedes ADR-0001 Addendum.
