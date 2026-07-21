# 08b — Logging (stderr + file + password obfuscation)

**What to build:** Structured logging to stderr (never stdout — stdout is the MCP JSON-RPC channel). Optional file sink via `MSSQL_LOG_FILE` env var. Plain text format with bracketed prefix convention: `[startup]`, `[tool]`, `[guard]`, `[sql]`, `[retry]`, `[connection]`, `[internal]`. `--log-level` flag (default `info`) and `MSSQL_LOG_LEVEL` env var (flag overrides env). Password obfuscation: regex-replace `Password=...;` with `Password=***;` on both stderr and file sinks via a custom logger filter. No file rotation in v1 (user provides exact path; configure logrotate externally). No new dependencies beyond `Microsoft.Extensions.Logging.Console` + BCL `Stream`.

**Blocked by:** 01 (Scaffold — needs the App project with DI)

**Status:** ready-for-agent

- [ ] Logger writes to stderr always (stdout is reserved for MCP JSON-RPC)
- [ ] Optional file sink when `MSSQL_LOG_FILE` env var is set (writes to the exact path, no rotation)
- [ ] `--log-level` flag: `trace`, `debug`, `info`, `warning`, `error`, `critical` (default `info`)
- [ ] `MSSQL_LOG_LEVEL` env var: same levels; `--log-level` flag overrides env var
- [ ] Bracketed prefix convention: `[startup]`, `[tool]`, `[guard]`, `[sql]`, `[retry]`, `[connection]`, `[internal]`
- [ ] Password obfuscation: regex `Password=[^;]*;` → `Password=***;` applied to all log entries on both sinks
- [ ] Custom logger filter implementation (no new dependencies — use `Microsoft.Extensions.Logging.Console` + BCL)
- [ ] Invalid `--log-level` value fails fast at startup
- [ ] Unit test: password obfuscation regex correctly replaces `Password=secret123;` → `Password=***;`
- [ ] Unit test: password obfuscation handles edge cases: `Password=;` (empty), `Password=p@ss;w0rd;` (special chars), `User Id=sa;Password=secret;` (mid-string)
- [ ] Unit test: log level filtering works (debug messages suppressed at info level)
- [ ] Integration test: verify nothing is written to stdout when logging is active
