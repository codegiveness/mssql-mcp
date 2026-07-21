# 10 — `--validate` flag

**What to build:** The user runs `mssql-mcp --validate --connection-string "Server=...;Database=...;User Id=...;Password=...;"` and the server opens a connection to SQL Server, closes it, prints `[startup] Connection validated successfully.` to stderr, and exits 0. On failure, prints `[startup] Connection validation failed: {error}` to stderr and exits 1. This lets the user verify their connection string before starting the MCP server — no more guessing whether the connection string is correct when Claude Desktop silently fails to connect.

**Blocked by:** 01 (Scaffold — needs `SqlExecutor` and `Options`)

**Status:** ready-for-agent

- [ ] `--validate` CLI flag parsed by `Options`
- [ ] When `--validate` is set: open a `SqlConnection`, run `SELECT 1`, close the connection, exit 0
- [ ] On success: print `[startup] Connection validated successfully.` to stderr
- [ ] On failure: print `[startup] Connection validation failed: {error message}` to stderr, exit 1
- [ ] Does NOT start the MCP stdio server (validate is a pre-flight check, not a server mode)
- [ ] Works with env var `MSSQL_CONNECTION_STRING` (env takes precedence over `--connection-string` flag per ADR-0015)
- [ ] Retry logic from ticket 08d applies (if 08d is done — otherwise just one attempt)
- [ ] Password obfuscated in any error output
- [ ] Unit test: `--validate` with valid connection string exits 0 and prints success message
- [ ] Unit test: `--validate` with invalid connection string exits 1 and prints error
- [ ] Integration test: `--validate` against real DB exits 0
