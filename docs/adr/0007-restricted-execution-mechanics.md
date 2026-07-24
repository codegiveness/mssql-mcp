# Restricted-mode execution: transaction + always rollback, configurable timeout, sentinel comment

## Context

Restricted-mode `execute_sql` needs a backstop for AST allowlist misses (ScriptDom parse edge cases, future T-SQL features) and a way to identify MCP-originated queries for DBAs.

## Decision

Restricted-mode `execute_sql` wraps every query in `BEGIN TRANSACTION ... ROLLBACK TRANSACTION` even when the AST allowlist has already approved it as SELECT/WITH only. The transaction rollback is a backstop for AST allowlist misses — the AST allowlist is the primary write-prevention layer, the rollback prevents the commit. Isolation level is SQL Server's default `READ COMMITTED` — agents need consistent reads, not dirty reads. Command timeout defaults to 30s in Restricted mode, 0=unlimited in Unrestricted, overridable via `--query-timeout` CLI flag or `MSSQL_QUERY_TIMEOUT` env var. Every query is prefixed with `/* mssql-mcp */` so DBAs can identify MCP-originated queries in `sys.dm_exec_requests` / `sys.dm_exec_sql_text`.

## Considered Options

- **No transaction, rely solely on AST allowlist.** Rejected: the allowlist is strong but not infallible. A no-op transaction + rollback on a SELECT costs nothing and catches the edge cases the allowlist might miss.
- **READ UNCOMMITTED + transaction + rollback** (dirty reads for exploration). Rejected: an agent might reason about uncommitted data incorrectly. `READ COMMITTED` is correct for agent-facing reads.
- **Hardcoded 30s timeout**. Rejected: a long-running `EXPLAIN` on a complex query or a heavy DMV aggregation in the Ops tier can legitimately exceed 30s. Users need the configurability knob.
- **Per-tool-call timeout** (agent passes `timeout_seconds` per `execute_sql`). Rejected: agents rarely know the right timeout for a query, and it adds an input parameter to every call. Server-level configuration is the right granularity.
- **No sentinel comment.** Rejected: zero cost, real DBA-side traceability value via `sys.dm_exec_requests`.

## Consequences

- Restricted-mode SELECTs run at the cost of one extra round-trip (the `BEGIN TRANSACTION` + `ROLLBACK` pair). Negligible for agent-driven workloads.
- If the AST allowlist ever misses a destructive statement (e.g. a future T-SQL feature introduces a new DDL path), the rollback prevents the commit. The agent sees a timeout or error, not a committed write.
- DBAs can `SELECT * FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) WHERE st.text LIKE '%mssql-mcp%'` to see what the MCP server is doing in real time.
- `--query-timeout` becomes the third CLI flag (after `--connection-string` and `--access-mode`). Keep the flag count low.
