# 05 — `explain_query`

**What to build:** The Agent calls `explain_query` with a SQL string and gets a summary execution plan — estimated cost, missing indexes, warnings, top 5 operations by cost — without the query executing. Optional `format: "xml"` returns raw SHOWPLAN_XML. The Guard validates the SQL in BOTH modes (Restricted and Unrestricted) — there is no legitimate reason to bypass plan analysis. The query is never executed; `SET SHOWPLAN_XML ON` is used instead. Critical implementation verification: confirm `Connection Reset=true` in Microsoft.Data.SqlClient clears the `SHOWPLAN_XML` session state when the connection returns to the pool, so subsequent queries on the same pooled connection don't silently return plans instead of results.

**Blocked by:** 02 (Guard: Restricted-mode AST validation)

**Status:** ready-for-agent

- [ ] `explain_query(sql, format?)` tool registered with `[McpServerTool(ReadOnly=true, Destructive=false)]`
- [ ] `format` parameter: enum `"summary"` (default) or `"xml"`
- [ ] Guard validates SQL in BOTH Restricted and Unrestricted modes (no bypass for explain)
- [ ] `SET SHOWPLAN_XML ON` executed before the query; query executed; `SET SHOWPLAN_XML OFF` after
- [ ] Transaction-wrapped (`BEGIN TRAN ... ROLLBACK`) in both modes
- [ ] Summary format parses SHOWPLAN_XML and extracts: estimated total cost, missing indexes (from the plan's `MissingIndex` elements), warnings, top 5 operations by estimated cost
- [ ] XML format returns the raw SHOWPLAN_XML as a string (subject to byte-cap safety net from ticket 08c)
- [ ] Rejection returns `GUARD_REJECTION` error (same as `execute_sql`)
- [ ] Unit test: fake `ISqlConnection` returns canned SHOWPLAN_XML; summary format extracts correct fields
- [ ] Integration test: `explain_query` on `SELECT * FROM sys.objects` returns plan with cost > 0
- [ ] Integration test (Oracle watch-out-for #2): after `explain_query`, a subsequent `execute_sql` returns rows (not plans) — verifies `Connection Reset=true` clears `SHOWPLAN_XML` state
- [ ] If `Connection Reset=true` does NOT clear the state: document the workaround (e.g. `pooling=false` for explain connections) in a code comment and note in the PR
