# mssql-mcp

[![CI](https://github.com/codegiveness/mssql-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/codegiveness/mssql-mcp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/mssql-mcp)](https://www.nuget.org/packages/mssql-mcp)
[![npm](https://img.shields.io/npm/v/mssql-mcp-cli)](https://www.npmjs.com/package/mssql-mcp-cli)
[![.NET](https://img.shields.io/badge/.NET-10-blue)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/github/license/codegiveness/mssql-mcp)](./LICENSE)

A Model Context Protocol (MCP) server for Microsoft SQL Server, built in C#/.NET 10. Lets AI agents (Claude Desktop, Cursor, etc.) safely query and interact with SQL Server through a controlled tool surface.

## Why this exists

The existing MSSQL MCP servers either expose raw SQL with no guardrails, ship broken/missing tools, or only target PostgreSQL. `mssql-mcp` gives agents a small, well-typed tool surface backed by AST validation, read-only transactions, timeouts, and a byte-size transport safety net — so an agent can explore schema, run SELECTs, and analyze query plans without a human in the loop, by default.

## Quick start (npm)

The npm package wraps a prebuilt self-contained .NET binary (the sqz pattern). `npx -y mssql-mcp-cli` works on Linux x64/arm64 and macOS x64/arm64 without installing .NET. Windows falls back to framework-dependent (requires .NET 10 runtime).

```jsonc
// Claude Desktop: ~/Library/Application Support/Claude/claude_desktop_config.json (macOS)
//                %APPDATA%\Claude\claude_desktop_config.json        (Windows)
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "npx",
      "args": ["-y", "mssql-mcp"],
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=localhost;Database=WideWorldImporters;User Id=sa;Password=YourStrong!Passw0rd;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

Restart Claude Desktop. Ask Claude: *"What databases do I have?"* and it should call `list_databases`.

## Quick start (dotnet tool)

If you have the .NET 10 SDK installed, install as a global tool. This is the recommended path on Windows (no SNI license concern — NuGet restore resolves `Microsoft.Data.SqlClient.SNI` on your machine) and works cross-platform.

```bash
dotnet tool install -g mssql-mcp
```

Then point your MCP client at the installed binary. With Claude Desktop:

```jsonc
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "mssql-mcp",
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=localhost;Database=WideWorldImporters;User Id=sa;Password=YourStrong!Passw0rd;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

## Access modes

mssql-mcp ships in two modes, selected at startup via `--access-mode` or `MSSQL_ACCESS_MODE`:

- **Restricted (default)** — read-only. The Guard enforces an AST allowlist, wraps every query in `BEGIN TRAN ... ROLLBACK`, applies a per-query command timeout, and truncates oversized results with a notice. All tools carry `readOnlyHint=true`. This is the mode to use with AI agents.
- **Unrestricted (opt-in)** — full DML/DDL via `execute_sql`. The Guard is bypassed for `execute_sql`, destructive operations carry `destructiveHint=true`, and the default query timeout is unlimited. Use this only when the human operator has explicitly authorized schema changes or writes. `explain_query` is still Guarded in both modes (it never executes the query).

```bash
mssql-mcp --access-mode unrestricted
# or
MSSQL_ACCESS_MODE=unrestricted mssql-mcp
```

## Tools

Nine tools, all `readOnlyHint=true` in Restricted mode. `execute_sql` gains `destructiveHint=true` in Unrestricted mode.

| Tool | Description |
|---|---|
| `list_databases` | List all databases with an `is_current` flag (system DBs excluded). |
| `list_schemas` | List schemas in the current or specified database. |
| `list_objects` | List tables/views/procedures/functions with schema, type, and limit filters (default 1000). |
| `get_object_details` | Return columns, parameters, indexes, and triggers for a specific object. |
| `execute_sql` | Execute a T-SQL batch. SELECT in Restricted; DML/DDL in Unrestricted. |
| `explain_query` | Show the execution plan summary (or raw XML) without executing the query. |
| `analyze_indexes` | Missing-index analysis from `sys.dm_db_missing_index_*` DMVs (workload-wide or per-query). |
| `get_top_queries` | Top queries by CPU/duration/reads from `sys.dm_exec_query_stats`. |
| `analyze_db_health` | Summary health check: size, VLFs, fragmentation, stats staleness, blocking. |

## Authentication

mssql-mcp uses the connection string you provide — it does not authenticate on its own. The connection string determines the auth mode.

### SQL Server authentication (username + password)

```text
Server=tcp:myserver.database.windows.net,1433;Database=mydb;User Id=sqladmin;Password=YourStrong!Passw0rd;Encrypt=True;TrustServerCertificate=False;
```

### Windows Integrated Authentication

On Windows with .NET 10, `Integrated Security=True` uses the running process's Windows credentials:

```text
Server=myserver;Database=mydb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;
```

This does not work from the self-contained Linux/macOS npm binary (no SSPI). For cross-platform, use SQL auth or Microsoft Entra ID.

### Microsoft Entra ID (formerly Azure AD) — Default authentication

`Authentication=Active Directory Default` picks up the ambient credential — managed identity in Azure, `az login` locally, or `DefaultAzureCredential` chain:

```text
Server=tcp:myserver.database.windows.net,1433;Database=mydb;Authentication=Active Directory Default;Encrypt=True;
```

For service principals with a client secret, use `Active Directory Service Principal` with `User ID` and `Password` set to the SPN client ID and secret respectively.

## Configuration

All runtime parameters are configurable via environment variable. Precedence: CLI flag (if present) > env var > hardcoded default. The single exception is `MSSQL_CONNECTION_STRING`, which takes precedence over `--connection-string` because secrets live in env, not argv. See [ADR-0015](./docs/adr/0015-configuration-via-env-vars.md) for the full rationale.

| Env var | Default | CLI flag | Purpose |
|---|---|---|---|
| `MSSQL_CONNECTION_STRING` | (none — required) | `--connection-string` (env wins) | SQL Server connection string |
| `MSSQL_ACCESS_MODE` | `restricted` | `--access-mode` | `restricted` or `unrestricted` |
| `MSSQL_QUERY_TIMEOUT` | `30` (restricted), `0` (unrestricted) | `--query-timeout` | Per-query command timeout in seconds; `0` = unlimited |
| `MSSQL_LOG_LEVEL` | `info` | `--log-level` | `trace` / `debug` / `info` / `warning` / `error` / `critical` |
| `MSSQL_LOG_FILE` | (stderr only) | (none) | Optional file path for log output |
| `MSSQL_MAX_RESULT_BYTES` | `10485760` (10 MB) | (none) | Result byte-size safety net; `0` disables |
| `MSSQL_RETRY_COUNT` | `3` | (none) | Transient-failure retry count (after first attempt) |
| `MSSQL_RETRY_INTERVAL` | `2` seconds | (none) | Min backoff for transient retries |
| `MSSQL_RETRY_INTERVAL_MAX` | `10` seconds | (none) | Max backoff for transient retries |

Invalid values fail fast at startup with a clear `[startup]` error naming the var, the invalid value, and the accepted range. Unknown env vars are ignored (forward compatibility).

## Installation

### Platform matrix

| RID | Self-contained | Archive | Notes |
|---|---|---|---|
| `linux-x64` | yes | `.tar.gz` | `npx -y mssql-mcp-cli` just works |
| `linux-arm64` | yes | `.tar.gz` | `npx -y mssql-mcp-cli` just works |
| `osx-x64` | yes | `.tar.gz` | Intel Macs |
| `osx-arm64` | yes | `.tar.gz` | Apple Silicon |
| `win-x64` | no (framework-dependent) | `.zip` | Requires .NET 10 runtime; `dotnet tool install` is the recommended path |

Windows is framework-dependent because the self-contained build would bundle `Microsoft.Data.SqlClient.SNI` under the Microsoft "Distributable Code" license, whose anti-copyleft clause conservatively blocks redistribution under our MIT license. Linux and macOS use the managed SNI implementation (MIT-clean). See [ADR-0002](./docs/adr/0002-distribution-strategy.md) for the full rationale.

### What `install.js` does

The npm package's `postinstall` script (`npm/install.js`) does the following on `npm install`:

1. Maps `process.platform` + `process.arch` to a RID. Unsupported platforms fail loudly with the list of supported RIDs and the `dotnet tool install -g mssql-mcp` fallback.
2. Downloads the flat archive for the resolved RID from the matching GitHub Release (`https://github.com/codegiveness/mssql-mcp/releases/download/v<version>/mssql-mcp-<version>-<rid>.tar.gz` or `.zip`).
3. Downloads the matching `.sha256` sidecar and verifies the archive hash.
4. Extracts the archive (flat — binary at archive root) and overwrites `npm/bin/mssql-mcp` with the real native binary.
5. `chmod 755` on Unix.
6. Prints the final binary path.

The fail-loudly contract (no silent fallback, no retry in v1) is documented in [ADR-0014](./docs/adr/0014-build-release-pipeline.md). If the download fails, the user must see a clear error naming the RID, the URL tried, and the `dotnet tool install` fallback.

### Windows note

`npx -y mssql-mcp-cli` on Windows will download a framework-dependent build. The first invocation requires the .NET 10 runtime to be installed. If you don't want to install the runtime, install the .NET tool instead:

```bash
dotnet tool install -g mssql-mcp
```

## Security

### Default is read-only

In Restricted mode (the default), every `execute_sql` call is wrapped in `BEGIN TRAN ... ROLLBACK`. Even if an agent crafts a destructive statement, the Guard rejects it before it reaches SQL Server, and even if the Guard allowed it, the transaction would roll back. Defense in depth.

### Guard layers

The Guard (see [ADR-0006](./docs/adr/0006-guard-ast-validation.md)) applies four layers of validation in Restricted mode:

1. **AST allowlist** — T-SQL is parsed by ScriptDom into an AST. A Visitor walks every batch and every nested statement, rejecting anything that isn't a SELECT (or a SELECT-adjacent statement on the allowlist). `INTO`, `OPENROWSET(BULK)`, `EXECUTE`, DDL, and DML are all rejected.
2. **Read-only transaction** — every query runs inside `BEGIN TRAN ... ROLLBACK`. Even a Guard bypass can't commit changes.
3. **Command timeout** — default 30s in Restricted mode. Runaway queries are killed.
4. **Byte-size safety net** — results over 10 MB (configurable) are truncated with a notice appended, so an agent's context window isn't blown out.

### Transaction rollback

In Unrestricted mode, the transaction wrapper is removed — that's what makes DML/DDL work. `explain_query` is still Guarded in both modes because it uses `SET SHOWPLAN_XML ON` and never executes the query.

### Reporting vulnerabilities

See [SECURITY.md](./SECURITY.md). Do not open a public issue for security vulnerabilities — use GitHub's private vulnerability reporting.

## Development

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git
- (Optional) Docker — for integration tests against Azure SQL Edge
- (Optional) Node 18+ — to run `npm/test.js` smoke tests

### Build and test

```bash
git clone https://github.com/codegiveness/mssql-mcp.git
cd mssql-mcp
dotnet restore
dotnet build
dotnet test --filter Category!=Integration
```

This is what CI runs. ~300 tests, completes in seconds.

### Integration tests (requires live SQL Server)

```bash
# Start Azure SQL Edge container
docker run -e "ACCEPT_EULA=1" -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" \
  -p 1433:1433 --name mssql-edge -d mcr.microsoft.com/azure-sql-edge:latest

# Run integration tests
INTEGRATION=true MSSQL_CONNECTION_STRING="Server=localhost;User Id=sa;Password=YourStrong!Passw0rd;Encrypt=True;TrustServerCertificate=True;" dotnet test
```

Integration tests are tagged `[Trait("Category", "Integration")]` and skipped by default.

### npm smoke test

```bash
node npm/test.js
```

Verifies `install.js` parses, the RID mapping returns expected values for known platforms, and the checksum parser handles bare and `sha256sum`-formatted sidecar files. The real integration test is `npm pack && npm install` on each platform.

### Project layout

```
mssql-mcp.sln
src/
  mssql-mcp.Core/       # Guard, SqlExecutor, Options — no MCP deps
  mssql-mcp.Tools/      # 9 tool classes with [McpServerTool]
  mssql-mcp/            # App: Program.cs, DI, stdio, CLI, npm entrypoint
tests/
  mssql-mcp.Core.Tests/ # Guard AST validation, type coercion, etc.
  mssql-mcp.Tools.Tests/ # Tool attribute wiring, schema tests
npm/                    # npm package: bin shim + install.js + smoke test
docs/adr/               # Architectural Decision Records
```

See [CONTRIBUTING.md](./CONTRIBUTING.md) for coding standards, ADR workflow, and the PR process.

## Trademarks & licensing

- **License:** MIT. See [LICENSE](./LICENSE) and [NOTICE](./NOTICE).
- **Third-party notices:** See [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md) for the full list, including `Microsoft.Data.SqlClient.SNI`'s "Distributable Code" license terms (which is why Windows builds are framework-dependent).
- **Not affiliated with Microsoft Corporation.** "Microsoft SQL Server", "Windows", "Azure", "Microsoft Entra ID", and related marks are trademarks of Microsoft Corporation. This project is an independent MCP server that connects to SQL Server using the public `Microsoft.Data.SqlClient` ADO.NET provider.
- **Client Access License (CAL) / multiplexing.** Using mssql-mcp does not reduce or eliminate SQL Server licensing requirements. Each end user or device that indirectly accesses SQL Server through mssql-mcp may require a CAL or a Core-based license, exactly as if they were connecting directly. You are responsible for ensuring your SQL Server deployment is properly licensed.

## Contributing

See [CONTRIBUTING.md](./CONTRIBUTING.md). PRs welcome — please open an issue first to discuss the scope of any non-trivial change.

## Stability

mssql-mcp is currently `0.x`. The tool surface is stable (tool names, parameter names, and parameter types don't break within the `0.x` series); CLI flags, env var names, error response shapes, and return value formats may change between minor versions before `1.0.0`. See [ADR-0014](./docs/adr/0014-build-release-pipeline.md#dual-stability-contract-0x--1000) for the dual stability contract.
