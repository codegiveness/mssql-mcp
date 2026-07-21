# 07 — Unrestricted mode + DML/DDL status objects

**What to build:** The user runs `mssql-mcp --access-mode unrestricted --connection-string "..."` and the Agent can call `execute_sql` with DML (`INSERT`/`UPDATE`/`DELETE`/`MERGE`) and DDL (`CREATE TABLE`/`DROP TABLE`/`ALTER`/etc.). The Guard is bypassed for `execute_sql` only (NOT for `explain_query` — that stays Guarded in both modes per ticket 05). The tool carries `destructiveHint=true` so the MCP client can warn before executing destructive operations. DML returns a status object with `rows_affected`; DDL returns a status object naming the affected object. `statement_type` is derived from the ScriptDom AST of the parsed statement.

**Blocked by:** 03 (`execute_sql` in Restricted mode)

**Status:** ready-for-agent

- [ ] `--access-mode unrestricted` (or `MSSQL_ACCESS_MODE=unrestricted`) bypasses Guard Layer 1 + Layer 2 for `execute_sql` only
- [ ] `execute_sql` tool annotation changes to `[McpServerTool(Destructive=true)]` when Unrestricted (or the tool checks mode at runtime and sets the hint dynamically — verify SDK supports this)
- [ ] No transaction wrapper in Unrestricted mode (queries commit immediately)
- [ ] Command timeout default 0 (unlimited) in Unrestricted mode, overridable via `MSSQL_QUERY_TIMEOUT`
- [ ] DML (`INSERT`/`UPDATE`/`DELETE`/`MERGE`): returns `[{"result":"success","statement_type":"UPDATE","rows_affected":42}]`
- [ ] DDL (`CREATE TABLE`/`DROP TABLE`/`ALTER`/etc.): returns `[{"result":"success","statement_type":"CREATE_TABLE","object":"dbo.NewTable"}]`
- [ ] `statement_type` derived from ScriptDom AST (parse the statement, read the concrete type name)
- [ ] `rows_affected` from `SqlDataReader.RecordsAffected`
- [ ] `object` extracted from the AST (table name for CREATE/DROP/ALTER)
- [ ] Multiple statements in one batch: return array of status objects, one per statement
- [ ] Unit test: fake `ISqlConnection`, `execute_sql` with `UPDATE` returns `rows_affected`
- [ ] Unit test: fake `ISqlConnection`, `execute_sql` with `CREATE TABLE` returns `object` name
- [ ] Integration test: `CREATE TABLE dbo.Test (id int)` then `DROP TABLE dbo.Test` against real DB
- [ ] `explain_query` still Guarded in Unrestricted mode (verify — no bypass)
