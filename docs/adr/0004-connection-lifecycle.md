# Single connection string at startup; rely on SqlClient built-in retry

## Context

The server needs a connection management strategy that handles transient failures without exposing connection complexity to the agent or the operator.

## Decision

The server reads one SQL Server connection string at startup (env var, config file, or CLI arg) and uses it for every tool call. The end user picks the database; the agent does not. SqlClient's built-in connection pool handles pooling. For transient failures (network blips, DB restarts), we configure `SqlRetryLogicOption` on each `SqlConnection` and rely on Microsoft's maintained transient-error list and retry logic — we do not write our own retry layer (a bespoke retry layer is a maintenance liability; Microsoft maintains the transient-error list).

Each executor operation owns its connection, commands, and reader, with reader disposal before commands and commands before the connection. Async disposal is used where supported; this is an ownership choice, not a claim that SqlClient disposal is inherently nonblocking. The executor owns one built-in retry provider and subscribes to its retry event once at construction, rather than adding handlers per request or mutating a process-global provider. Microsoft's provider uses a concurrent pool of cloned retry state for concurrent operations ([implementation](https://github.com/dotnet/SqlClient/blob/main/src/Microsoft.Data.SqlClient/src/Microsoft/Data/SqlClient/Reliability/Common/SqlRetryLogicProvider.cs)).

SHOWPLAN cleanup runs after reader disposal and ignores the canceled request token so `SET SHOWPLAN_XML OFF` can restore session state. Its separate cancellation budget is five seconds, and its command timeout is the smaller of a positive query timeout and five seconds (five seconds when the query timeout is zero). This prevents the configured unlimited query timeout from also making cleanup unlimited; SqlClient must still acknowledge cancellation, so this is not a hard wall-clock bound on all disposal or network failure handling. If OFF fails, the affected pool is cleared and the original query outcome is preserved. [SqlClient's pool-clearing contract](https://learn.microsoft.com/dotnet/api/microsoft.data.sqlclient.sqlconnection.clearpool) discards checked-out connections when they are returned rather than recycling the session whose state is uncertain.

Row coercion reuses a provider-value scratch array within one query, while each returned row remains an independent dictionary. The scratch array is never shared across operations or retained in a result. Raw plan byte counting is skipped when its limit is disabled; cancellation checks and XML retention remain in effect.

## Considered Options

- Per-call connection string — rejected: credentials in every tool call is insecure and forces the agent to carry connection state.
- `use_database` tool for mid-session switching — rejected: which DB to target is a human decision, not an agent decision. Multi-DB users run multiple server instances.
- Our own retry wrapper around `Open()` + command execution — rejected: Microsoft maintains the transient-error list; re-implementing risks staleness and bugs.
- No retry, surface errors to agent — rejected: agents misdiagnose transient errors as query bugs and try to "fix" the SQL.

## Consequences

- Connection string is read once at startup. If the DB moves or creds rotate, restart the server.
- Agent can still query other DBs on the same server via `USE` or three-part names (`SELECT * FROM OtherDb.dbo.Table`) — that's SQL Server's native multi-DB model and we don't need to replicate it.
- `list_databases` lets the agent *see* other DBs; switching to them is an end-user config change.
- Retry defaults hardcoded (retry 3, backoff 2–10s), overridable via env vars `MSSQL_RETRY_COUNT` / `MSSQL_RETRY_INTERVAL` — not on CLI. CLI flags stay focused on the common case.
- If retries are exhausted, the `SqlException` surfaces to the agent. The agent's natural behavior on a transient error is to retry the tool call, which is correct.
