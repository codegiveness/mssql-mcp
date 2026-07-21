# Every runtime parameter is configurable via environment variable

Every runtime-tunable parameter in mssql-mcp is overridable via an environment variable. CLI flags exist for the startup-shape decisions an operator makes interactively (`--access-mode`, `--connection-string`, `--query-timeout`, `--log-level`, `--validate`, `--version`, `--help`); env vars exist for *every* tunable, including the ones without a CLI flag, because containerized and MCP-client-hosted deployments pass config via env vars far more easily than via argv.

**Precedence**: CLI flag (if present) > env var > hardcoded default. The single exception is `MSSQL_CONNECTION_STRING`, which takes precedence over the `--connection-string` flag (env-secret-over-flag) — this matches the principle that secrets live in env, not argv.

**The complete env var surface for v1**:

| Env var | Default | CLI flag? | Purpose |
|---|---|---|---|
| `MSSQL_CONNECTION_STRING` | (none — required) | `--connection-string` (env wins) | SQL Server connection string (ADR-0004, ADR-0005) |
| `MSSQL_ACCESS_MODE` | `restricted` | `--access-mode` | `restricted` or `unrestricted` (ADR-0001) |
| `MSSQL_QUERY_TIMEOUT` | `30` (restricted), `0` (unrestricted) | `--query-timeout` | Per-query command timeout in seconds; `0` = unlimited (ADR-0007) |
| `MSSQL_LOG_LEVEL` | `info` | `--log-level` | `trace`, `debug`, `info`, `warning`, `error`, `critical` (ADR-0011) |
| `MSSQL_LOG_FILE` | (stderr only) | (none) | Optional file path for log output (ADR-0011) |
| `MSSQL_MAX_RESULT_BYTES` | `10485760` (10 MB) | (none) | Result byte-size safety net; `0` disables (ADR-0003) |
| `MSSQL_RETRY_COUNT` | `3` | (none) | Transient-failure retry count passed to `SqlRetryLogicOption` (ADR-0004) |
| `MSSQL_RETRY_INTERVAL` | `2` (min), `10` (max) seconds | (none) | Backoff range for transient retries (ADR-0004) |

**Rules**:
- **No magic defaults without env escape hatches.** If a value is tunable at runtime, it gets an env var. Hardcoded constants are reserved for things that *cannot* be different at runtime (e.g., the `/* mssql-mcp */` sentinel comment, the AST allowlist, the version string).
- **Env var names use the `MSSQL_` prefix** consistently.
- **Invalid env var values** fail fast at startup with a clear `[startup]` error naming the var, the invalid value, and the accepted range. The process exits non-zero. This is the same contract as `--validate`.
- **Unknown env vars** are ignored, not errors — forward compatibility for future flags.
- **Boolean env vars** accept `1`/`true`/`yes`/`on` (case-insensitive) as true; anything else is false. Numeric env vars accept integers only.
