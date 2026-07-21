# 01 — Scaffold + `list_databases` end-to-end

**What to build:** The first complete vertical slice. The user runs `mssql-mcp --connection-string "Server=...;Database=...;User Id=...;Password=...;"` and an Agent calls `list_databases` and gets a JSON array of databases with the `is_current` flag. This ticket establishes every pattern that subsequent tickets follow: git init, solution + 3 projects (Core/Tools/App), Options (env+CLI with precedence), basic SqlExecutor (no Guard, no retry yet), type coercion, lean JSON array return shape, stdio transport via MCP SDK, xUnit test project, CI skeleton (ci.yml). The `list_databases` tool queries `sys.databases` with the `is_current` computed column and excludes system DBs + `mssqlsystemresource`.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] Git repo initialized with `.gitignore` for .NET + npm + IDE files
- [ ] `mssql-mcp.sln` solution with 3 projects: `src/mssql-mcp.Core`, `src/mssql-mcp.Tools`, `src/mssql-mcp` (App)
- [ ] 2 test projects: `tests/mssql-mcp.Core.Tests`, `tests/mssql-mcp.Tools.Tests` (xUnit)
- [ ] Core project references: `Microsoft.Data.SqlClient`, `Microsoft.SqlServer.TransactSql.ScriptDom`, `Microsoft.Extensions.Options`
- [ ] Tools project references: Core + `ModelContextProtocol` SDK (v1.4.1)
- [ ] App project references: Tools
- [ ] `Options` class parses env vars (`MSSQL_CONNECTION_STRING`, `MSSQL_ACCESS_MODE`) and CLI flags (`--connection-string`, `--access-mode`); env var wins for connection string
- [ ] `SqlExecutor` opens a connection from the connection string and executes a query, returning rows as `List<Dictionary<string,object>>` (type-coerced per ADR-0009)
- [ ] `list_databases` tool registered with `[McpServerTool(ReadOnly=true, Destructive=false)]`, returns JSON array as `TextContent`
- [ ] Type coercion implemented for all types in ADR-0009 (bigint/decimal as string, dates as ISO 8601, binary as base64, etc.)
- [ ] App `Program.cs` builds DI container, registers tools, starts stdio server
- [ ] Unit test: fake `ISqlConnection` returns canned databases, `list_databases` returns correct JSON
- [ ] Integration test (tagged `[Trait("Category","Integration")]`): real DB, `list_databases` returns at least `master` + the connected database with `is_current=true`
- [ ] `ci.yml` workflow: `dotnet build` + `dotnet test --filter Category!=Integration` on push to main and PRs
- [ ] Invalid `--access-mode` value fails fast at startup with clear error
- [ ] Missing connection string fails fast at startup with clear error
