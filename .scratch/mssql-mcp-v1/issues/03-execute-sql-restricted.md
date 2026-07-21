# 03 — `execute_sql` in Restricted mode

**What to build:** The Agent calls `execute_sql` with a SQL string and gets results. In Restricted mode, the Guard validates the SQL (Layer 1 + Layer 2), wraps it in `BEGIN TRAN ... ROLLBACK`, applies the 30s timeout, prefixes the sentinel comment, executes, and returns the result as a lean JSON array of objects. If the Guard rejects the SQL, the Agent gets a structured `GUARD_REJECTION` error. This is the first SQL execution tool — it proves the full pipeline: tool call → Guard → SqlExecutor → type coercion → return shape.

**Blocked by:** 02 (Guard: Restricted-mode AST validation)

**Status:** ready-for-agent

- [ ] `execute_sql` tool registered with `[McpServerTool(ReadOnly=true, Destructive=false)]` in Tools project
- [ ] Tool accepts single `sql` string parameter (no `parameters[]` array)
- [ ] In Restricted mode: Guard validates SQL before execution
- [ ] On Guard rejection: returns `isError: true` with structured `GUARD_REJECTION` JSON
- [ ] On Guard acceptance: wraps SQL in `BEGIN TRANSACTION ... ROLLBACK TRANSACTION`
- [ ] Sentinel comment `/* mssql-mcp */` prefixed
- [ ] Command timeout (default 30s) applied
- [ ] Results returned as lean JSON array of objects per ADR-0009 (type coercion already implemented in ticket 01)
- [ ] Empty result returns `[]` (not an error)
- [ ] Unit test: fake `ISqlConnection`, `execute_sql` with `SELECT 1` returns `[{"":1}]`
- [ ] Unit test: fake `ISqlConnection` throws `SqlException`, returns structured `SQL` error with code/severity/line
- [ ] Unit test: fake `ISqlConnection` throws `OperationCanceledException`, returns structured `TIMEOUT` error
- [ ] Integration test: `execute_sql` with `SELECT * FROM sys.objects WHERE type='U'` returns rows against real DB
- [ ] Integration test: `execute_sql` with `DROP TABLE x` in Restricted mode returns `GUARD_REJECTION` and does not execute
