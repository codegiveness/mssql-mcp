# Authentication: SQL password + Windows Integrated + Active Directory Default; env var + CLI flag

v1 supports three auth methods: SQL password (universal baseline), Windows Integrated (`Integrated Security=SSPI`, Windows-only via `dotnet tool` / framework-dependent path — SNI license blocks self-contained Windows builds per ADR-0002), and Active Directory Default (`Authentication=Active Directory Default`, Microsoft's recommended "do the right thing" chain — covers MSI, VS, CLI, Interactive fallbacks). This covers the standard SQL Server authentication matrix. We skip AD Password and AD Service Principal in v1 — anyone needing them can use AD Default via environment variables, and we can add them in v1.1 if requested.

Connection string is supplied via env var `MSSQL_CONNECTION_STRING` or CLI flag `--connection-string`, with env var taking precedence. No config file in v1. Env var matches the MCP host config pattern (Claude Desktop, Cursor inject env vars); CLI flag helps debugging and `npx` one-shots. Connection string details are never logged raw — `Password=...;` is regex-replaced with `Password=***;` in all log output.

**Considered Options**:
- SQL password only — rejected: excludes every corporate Windows user on Integrated Auth and every Azure-hosted MSI scenario.
- Everything except Interactive — rejected: AD Password and AD Service Principal add code paths and testing burden for marginal v1 value; AD Default covers most of their use cases via environment variables.
- Per-call connection string — rejected: credentials in every tool call is insecure; agent carries connection state in its reasoning context.
- Config file (`.mssql-mcp.json`) — rejected for v1: file-location resolution adds complexity for little value when env var + CLI flag cover the MCP host config patterns.

**Consequences**:
- Linux/macOS self-contained binaries: SQL password + AD Default work cleanly (managed SNI, MIT-clean). Windows Integrated does NOT work on these platforms — SSPI is Windows-only, and Kerberos-on-Linux is fragile and out of scope for v1.
- Windows: all three auth methods work via `dotnet tool install` or framework-dependent execution (SNI present on user's machine via NuGet restore, we are not the distributor).
- Agent never sees credentials — connection string is a server config concern, not a tool input.
- If creds rotate or DB moves, restart the server (per ADR-0004).
