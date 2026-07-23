# Single connection string at startup; rely on SqlClient built-in retry

The server reads one SQL Server connection string at startup (env var, config file, or CLI arg) and uses it for every tool call. The end user picks the database; the agent does not. SqlClient's built-in connection pool handles pooling. For transient failures (network blips, DB restarts), we configure `SqlRetryLogicOption` on each `SqlConnection` and rely on Microsoft's maintained transient-error list and retry logic — we do not write our own retry layer (a bespoke retry layer is a maintenance liability; Microsoft maintains the transient-error list).

**Considered Options**:
- Per-call connection string — rejected: credentials in every tool call is insecure and forces the agent to carry connection state.
- `use_database` tool for mid-session switching — rejected: which DB to target is a human decision, not an agent decision. Multi-DB users run multiple server instances.
- Our own retry wrapper around `Open()` + command execution — rejected: Microsoft maintains the transient-error list; re-implementing risks staleness and bugs.
- No retry, surface errors to agent — rejected: agents misdiagnose transient errors as query bugs and try to "fix" the SQL.

**Consequences**:
- Connection string is read once at startup. If the DB moves or creds rotate, restart the server.
- Agent can still query other DBs on the same server via `USE` or three-part names (`SELECT * FROM OtherDb.dbo.Table`) — that's SQL Server's native multi-DB model and we don't need to replicate it.
- `list_databases` lets the agent *see* other DBs; switching to them is an end-user config change.
- Retry defaults hardcoded (retry 3, backoff 2–10s), overridable via env vars `MSSQL_RETRY_COUNT` / `MSSQL_RETRY_INTERVAL` — not on CLI. CLI flags stay focused on the common case.
- If retries are exhausted, the `SqlException` surfaces to the agent. The agent's natural behavior on a transient error is to retry the tool call, which is correct.
