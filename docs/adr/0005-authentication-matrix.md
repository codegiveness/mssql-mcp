# Authentication: SQL password + Windows Integrated; env var + CLI flag

## Context

The server needs to support the standard SQL Server authentication matrix across platforms, while keeping credentials out of the agent's context and out of command-line arguments.

## Decision

Support SQL password (universal baseline) and Windows Integrated (`Integrated Security=SSPI`, Windows-only via `dotnet tool` / framework-dependent execution). Windows uses managed SNI with native SNI assets excluded per ADR-0002. Microsoft Entra connection-string authentication is no longer supported following the owner-authorized dependency removal below.

Connection string is supplied via env var `MSSQL_CONNECTION_STRING` or CLI flag `--connection-string`, with env var taking precedence. No config file in v1. Env var matches the MCP host config pattern (Claude Desktop, Cursor inject env vars); CLI flag helps debugging and `npx` one-shots. Connection string details are never logged raw — `Password=...;` is regex-replaced with `Password=***;` in all log output.

## SqlClient 7 licensing cutover

The current SqlClient 7.1.1 baseline separates driver-provided Entra authentication
into `Microsoft.Data.SqlClient.Extensions.Azure`. Supported extension versions
and the checked SqlClient 6.1 LTS rollback still depend on the restricted native
broker. Disabling broker usage does not remove the package from published assets.
The owner authorized removal rather than licensing clearance; introducing a
custom authentication subsystem or downgrading security fixes was not warranted.

Remove the Azure extension, its trimming root, and its transitive authentication
graph. This removes every `Authentication=Active Directory ...` connection-string
mode, including Default and managed identity. SQL password and Windows Integrated
Authentication remain. See [Third-Party Notices](../../THIRD-PARTY-NOTICES.md) and
the [distribution decision](0002-distribution-strategy.md#entra-dependency-removal).

## Considered Options

- SQL password only — rejected: excludes corporate Windows users on Integrated Authentication.
- Per-call connection string — rejected: credentials in every tool call is insecure; agent carries connection state in its reasoning context.
- Config file (`.mssql-mcp.json`) — rejected for v1: file-location resolution adds complexity for little value when env var + CLI flag cover the MCP host config patterns.

## Consequences

- Linux/macOS self-contained binaries: SQL password is supported (managed SNI). Windows Integrated is not supported on these platforms; SSPI is Windows-only and Kerberos-on-Linux remains out of scope.
- Windows: SQL password and Windows Integrated remain supported via `dotnet tool install` or framework-dependent execution using SqlClient's managed networking implementation. Native SNI is excluded from published assets.
- Agent never sees credentials — connection string is a server config concern, not a tool input.
- If creds rotate or DB moves, restart the server (per ADR-0004).
