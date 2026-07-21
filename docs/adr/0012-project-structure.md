# Project structure: multi-project layered

Three-project solution separating pure logic (Core), MCP tool surface (Tools), and application wiring (App). Core has no MCP SDK dependency, making the Guard and SQL execution layer testable in isolation — the highest-value test surface, since Guard correctness is the safety claim of the whole project.

## Layout

```
mssql-mcp/
├── mssql-mcp.sln
├── src/
│   ├── mssql-mcp.Core/           # Guard, SqlExecutor, types — no MCP deps
│   │   ├── Guard/
│   │   │   ├── AstValidator.cs
│   │   │   └── ExecutionWrapper.cs
│   │   ├── Data/
│   │   │   └── SqlExecutor.cs
│   │   ├── Options/
│   │   │   └── MssqlMcpOptions.cs
│   │   └── mssql-mcp.Core.csproj
│   ├── mssql-mcp.Tools/          # [McpServerTool] classes — refs Core + MCP SDK
│   │   ├── Discovery/
│   │   │   ├── ListDatabasesTool.cs
│   │   │   ├── ListSchemasTool.cs
│   │   │   ├── ListObjectsTool.cs
│   │   │   └── GetObjectDetailsTool.cs
│   │   ├── Sql/
│   │   │   ├── ExecuteSqlTool.cs
│   │   │   └── ExplainQueryTool.cs
│   │   ├── Ops/
│   │   │   ├── AnalyzeQueryIndexesTool.cs
│   │   │   ├── AnalyzeWorkloadIndexesTool.cs
│   │   │   ├── GetTopQueriesTool.cs
│   │   │   └── AnalyzeDbHealthTool.cs
│   │   └── mssql-mcp.Tools.csproj
│   └── mssql-mcp/                # Program.cs, DI, npm wrapper entrypoint
│       ├── Program.cs
│       ├── mssql-mcp.csproj      # Packable as dotnet tool package
│       └── appsettings.json
├── tests/
│   ├── mssql-mcp.Core.Tests/
│   └── mssql-mcp.Tools.Tests/
├── npm/                          # npm wrapper package (sqz pattern)
│   ├── package.json
│   ├── install.js
│   └── bin/mssql-mcp             # Node.js shim, overwritten by install.js
└── docs/adr/
```

## Project graph

```
mssql-mcp.Core  ←── mssql-mcp.Tools  ←── mssql-mcp (App)
                     [McpServerTool]       [Program.cs, DI, npm shim]
```

- **Core**: `Microsoft.Data.SqlClient`, `Microsoft.SqlServer.TransactSql.ScriptDom`, `Microsoft.Extensions.Options`. No MCP SDK. No `Microsoft.Extensions.Hosting`.
- **Tools**: `ModelContextProtocol` (ADR-0008), `mssql-mcp.Core`. Tool classes only — no business logic, no SQL execution.
- **App**: `Microsoft.Extensions.Hosting`, `mssql-mcp.Tools`, `mssql-mcp.Core`. DI wiring, logging setup, stdio transport, CLI arg parsing, npm entrypoint.

## Naming

- Solution: `mssql-mcp.sln` (root)
- Projects: `mssql-mcp.Core`, `mssql-mcp.Tools`, `mssql-mcp` (App — no suffix, matches NuGet/dotnet-tool package name)
- Test projects: `mssql-mcp.Core.Tests`, `mssql-mcp.Tools.Tests`
- Test framework: **xUnit** (matches MCP SDK's own tests, Microsoft's ASP.NET Core / EF Core tests, dominant in .NET 2026)
- npm package: `mssql-mcp` (matches dotnet tool name for cross-channel consistency)
- GitHub repo: `mssql-mcp`

## Considered Options

- **B. Multi-project layered** ✅ — chosen
- A. Single-project flat — rejected: forces Guard tests to drag in MCP SDK; dead-code risk (c0h1b4 had 400 lines of unused `src/utils/` files); weaker signal of intent for public OSS
- C. Single-project + npm subdir only — rejected: same problems as A

## Consequences

- Core tests instantiate `AstValidator` and `SqlExecutor` directly — no MCP transport, no stdio, no hosting. Fastest, highest-signal test loop.
- Tools tests verify `[McpServerTool]` attribute wiring and input schema shape — can use the SDK's `McpServerTool` introspection without booting a full server.
- Cross-project references enforce wiring at compile time — if `Validation.cs` isn't referenced by Tools or App, the build fails. Prevents c0h1b4's dead-code pattern structurally.
- Three `.csproj` files (~30 lines of XML total) is the cost. Negligible.
- If we ever swap the MCP SDK (ADR-0008 v2 upgrade), blast radius is one project (Tools).
