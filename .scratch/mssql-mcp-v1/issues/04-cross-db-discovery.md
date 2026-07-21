# 04 — Cross-DB safety + remaining discovery tools

**What to build:** The Agent can call `list_schemas(database?)`, `list_objects(database?, schema?, type?, limit?)`, and `get_object_details(database?, schema, name, type?)` against the current database or a specified one. Cross-database queries work safely: the `database` parameter is validated against `sys.databases` for existence, online state, and multi-user access. Database names are injected via bracketed identifiers with internal `]` doubled (`QuoteIdentifier`). `list_objects` filters `is_ms_shipped=0`, defaults `limit=1000` (max 5000), maps the short type enum to `sys.objects.type` char codes, and prepends a truncation notice when the limit is hit. `get_object_details` returns columns/parameters/indexes/triggers for the specified object, or an explicit `OBJECT_NOT_FOUND` error on zero rows.

**Blocked by:** 01 (Scaffold + `list_databases`)

**Status:** ready-for-agent

- [ ] `QuoteIdentifier(string name)` helper in Core: wraps in `[...]` and doubles internal `]` → `]]`
- [ ] Unit test: `QuoteIdentifier("my]db")` returns `[my]]db]`
- [ ] Unit test: `QuoteIdentifier("normal")` returns `[normal]`
- [ ] Unit test: `QuoteIdentifier("my]weird]db]")` returns `[my]]weird]]db]]]`  (Oracle watch-out-for #1)
- [ ] `ValidateDatabase(string database)` in Core: checks `sys.databases` for (1) exists, (2) `state_desc='ONLINE'`, (3) `user_access_desc='MULTI_USER'`; returns specific error naming which check failed
- [ ] `list_schemas(database?)` tool: `SELECT name, schema_id FROM {db}.sys.schemas ORDER BY schema_id` (includes system schemas)
- [ ] `list_objects(database?, schema?, type?, limit?)` tool: `SELECT TOP (@limit) name, schema_name(schema_id) AS [schema], type_desc AS [type] FROM {db}.sys.objects WHERE is_ms_shipped=0 [AND schema_id=SCHEMA_ID(@schema)] [AND type IN (...)] ORDER BY schema_name(schema_id), name`
- [ ] Type enum maps to char codes: `"TABLE"`→`'U'`, `"VIEW"`→`'V'`, `"PROCEDURE"`→`('P','PC')`, `"FUNCTION"`→`('FN','IF','TF','FS','FT')`
- [ ] `limit` default 1000, max 5000, protects Agent context window
- [ ] Truncation notice prepended as first array element when limit hit: `{"truncated":true,"returned":1000,"note":"Results truncated. Refine schema/type filters or raise limit."}`
- [ ] `get_object_details(database?, schema, name, type?)` tool: returns columns (tables/views), parameters (procedures/functions), indexes (tables), triggers
- [ ] On zero rows: returns `{"error":"OBJECT_NOT_FOUND","schema":"...","name":"...","type":"...","database":"..."}` with `isError: true`
- [ ] All 3 tools registered with `[McpServerTool(ReadOnly=true, Destructive=false)]`
- [ ] Unit tests for each tool (fake `ISqlConnection` with canned results)
- [ ] Integration tests against real DB with known schema
