# 02 — Guard: Restricted-mode AST validation

**What to build:** The Guard that makes Restricted mode safe. Parses T-SQL with ScriptDom's `TSql160Parser.Parse()` (returns `TSqlScript` with `Batches` collection) and uses a `TSqlFragmentVisitor` to reject any statement that is not `SelectStatement` anywhere in the AST — including multi-statement batches (`SELECT 1; DROP TABLE x`), GO-separated batches (`SELECT 1 GO DROP TABLE x`), and nested statements (`BEGIN DROP TABLE x END`, `IF (1=1) DROP TABLE x`). Layer 2 rejects `SELECT ... INTO`, `OPENROWSET`/`OPENDATASOURCE`/`OPENQUERY`/`OPENXML`, `EXECUTE AS`, four-part (linked-server) names, `BulkInsertStatement`. Also adds the transaction wrapper (`BEGIN TRAN ... ROLLBACK`), 30s command timeout, and `/* mssql-mcp */` sentinel comment. Every rejection returns a structured `GUARD_REJECTION` error with a `rule` field naming the specific rule violated.

**Blocked by:** 01 (Scaffold + `list_databases`)

**Status:** ready-for-agent

- [ ] `Guard` class in Core takes a SQL string, parses with `TSql160Parser.Parse()`, returns accept/reject result
- [ ] Layer 1: `TSqlFragmentVisitor` overrides catch-all to record every statement type; reject if set contains anything other than `SelectStatement`
- [ ] Visitor recurses into `BeginEndBlockStatement`, `IfStatement`, `WhileStatement` (automatic via `script.Accept(visitor)`)
- [ ] `SelectStatement` is the unifying type for bare `SELECT` and `WITH ... SELECT` (CTE via `WithCtesAndXmlNamespaces`)
- [ ] `WITH ... DELETE/INSERT/UPDATE/MERGE` correctly rejected (produces `DeleteStatement` etc., not `SelectStatement`)
- [ ] `TSqlStatementSnippet` rejected explicitly
- [ ] Empty batches (0 statements) rejected with `[guard] No executable statement found.`
- [ ] Parse errors rejected with line and column
- [ ] Layer 2: `SELECT ... INTO` rejected (check `node.Into` on `SelectStatement`)
- [ ] Layer 2: `OPENROWSET`/`OPENDATASOURCE`/`OPENQUERY`/`OPENXML` in FROM rejected
- [ ] Layer 2: `EXECUTE AS` rejected
- [ ] Layer 2: four-part (linked-server) names rejected
- [ ] Layer 2: `BulkInsertStatement` rejected
- [ ] Rejection returns structured JSON: `{"error":"GUARD_REJECTION","rule":"<rule>","detail":"...","statement_type":"...","position":{"line":N,"column":N}}`
- [ ] Transaction wrapper: every Restricted-mode query wrapped in `BEGIN TRANSACTION ... ROLLBACK TRANSACTION`
- [ ] Command timeout: default 30s in Restricted mode (configurable via `MSSQL_QUERY_TIMEOUT`)
- [ ] `/* mssql-mcp */` sentinel comment prefixed to every query
- [ ] Unit tests (direct Guard calls, not through tool seam) for ALL attack vectors:
  - `SELECT 1` (accept)
  - `SELECT 1; DROP TABLE x` (reject: multi-statement)
  - `SELECT 1 GO DROP TABLE x` (reject: GO-separated)
  - `BEGIN DROP TABLE x END` (reject: nested in BEGIN/END)
  - `IF (1=1) DROP TABLE x` (reject: nested in IF)
  - `WHILE (1=1) DROP TABLE x` (reject: nested in WHILE)
  - `WITH cte AS (SELECT 1) SELECT * FROM cte` (accept)
  - `WITH cte AS (SELECT 1) DELETE FROM cte` (reject: CTE with DELETE)
  - `SELECT * INTO #temp FROM Users` (reject: SELECT INTO)
  - `SELECT * FROM OPENROWSET(...)` (reject: OPENROWSET)
  - `SELECT * FROM OPENQUERY(link, '...')` (reject: OPENQUERY)
  - `SELECT * FROM OPENXML(...)` (reject: OPENXML)
  - `SELECT * FROM OPENDATASOURCE(...)` (reject: OPENDATASOURCE)
  - `EXECUTE AS USER = 'sa'` (reject: EXECUTE AS)
  - `SELECT * FROM [server].[db].[dbo].[table]` (reject: four-part name)
  - `BULK INSERT ...` (reject: BulkInsertStatement)
  - `-- DROP TABLE x` (accept: comment is not a statement)
  - `` (empty string — reject: empty batch)
  - `SELECT !!!` (reject: parse error with line/column)
  - `TSqlStatementSnippet` edge case (reject)
- [ ] Integration test: `INSERT` in Restricted mode rolls back (verify rowcount 0 after)
