# Tool input schemas (9 tools, v1)

The v1 surface is 9 tools across 3 groups: Discovery (4), SQL (2), Ops (3). Every tool that accepts a `database` parameter must validate it via the three-check cross-DB safety rule (see "Cross-database query safety" below).

## Discovery tools

### `list_databases()`

No parameters. Returns one row per database (excluding system DBs and `mssqlsystemresource`) with an `is_current` bit column so the Agent can orient on its first call.

```sql
SELECT name, database_id, state_desc,
       CASE WHEN name = DB_NAME() THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS is_current
FROM sys.databases
WHERE database_id > 4 AND database_id < 32767
ORDER BY is_current DESC, name
```

### `list_schemas(database?)`

| Param | Type | Default | Notes |
|---|---|---|---|
| `database` | string? | current | Cross-DB-validated |

Returns all schemas (including system schemas — Agents sometimes need `sys.*` views), sorted by `schema_id` (puts `dbo`=1 first, system schemas at end).

```sql
SELECT name, schema_id FROM {db}.sys.schemas ORDER BY schema_id
```

### `list_objects(database?, schema?, type?, limit?)`

| Param | Type | Default | Max | Notes |
|---|---|---|---|---|
| `database` | string? | current | — | Cross-DB-validated |
| `schema` | string? | all | — | Filters via `schema_id = SCHEMA_ID(@schema)` (not `schema_name() = @schema` — avoids per-row function call) |
| `type` | enum? | all | — | `"TABLE"`→`type='U'`, `"VIEW"`→`type='V'`, `"PROCEDURE"`→`type IN ('P','PC')`, `"FUNCTION"`→`type IN ('FN','IF','TF','FS','FT')` |
| `limit` | int? | 1000 | 5000 | Protects Agent context window, not transport (10MB byte cap in ADR-0003 handles transport) |

Filters out Microsoft-shipped objects (`is_ms_shipped = 0`). When truncated, prepend a truncation notice object before the rows.

```sql
SELECT TOP (@limit) name, schema_name(schema_id) AS [schema], type_desc AS [type]
FROM {db}.sys.objects
WHERE is_ms_shipped = 0
  [AND schema_id = SCHEMA_ID(@schema)]
  [AND type IN (/* mapped enum values */)]
ORDER BY schema_name(schema_id), name
```

Truncation notice shape (prepended as first array element): `{"truncated": true, "returned": 1000, "note": "Results truncated. Refine schema/type filters or raise limit."}`

### `get_object_details(database?, schema, name, type?)`

| Param | Type | Default | Notes |
|---|---|---|---|
| `database` | string? | current | Cross-DB-validated |
| `schema` | string | (required) | |
| `name` | string | (required) | |
| `type` | enum? | auto | Same enum as `list_objects`; if omitted, match any type |

Returns object metadata: columns (for tables/views), parameters (for procedures/functions), indexes (for tables), triggers. Exact query varies by object type — this tool encapsulates the complexity so the Agent doesn't need to know `sys.columns` vs `sys.parameters` vs `sys.indexes`.

On zero rows: return `{"error": "OBJECT_NOT_FOUND", "schema": "...", "name": "...", "type": "...", "database": "..."}` per ADR-0010. Empty array is ambiguous for object lookup.

## SQL tools

### `execute_sql(sql)`

| Param | Type | Notes |
|---|---|---|
| `sql` | string (required) | Single T-SQL batch. Guard (ADR-0006) validates in Restricted mode. |

No `parameters[]` array. In MCP, the Agent IS the query author — no traditional SQL injection vector. The three real risks (destructive SQL, resource exhaustion, unauthorized access) are handled by Guard + timeout + byte cap + DB permissions.

In Restricted mode: Guard AST allowlist + `BEGIN TRAN ... ROLLBACK` wrapper. In Unrestricted mode: DDL/DML permitted, returns non-rowset status objects per ADR-0009 (`[{"result": "success", "statement_type": "UPDATE", "rows_affected": 42}]`).

### `explain_query(sql, format?)`

| Param | Type | Default | Notes |
|---|---|---|---|
| `sql` | string (required) | | The query to get an execution plan for |
| `format` | enum? | `"summary"` | `"summary"` (estimated cost, missing indexes, warnings, top 5 ops by cost) or `"xml"` (raw SHOWPLAN_XML) |

Guard validates in **both** modes (Unrestricted mode skips the read-only guardrail for `execute_sql` but NOT for `explain_query` — there is no legitimate reason to bypass plan analysis). Transaction-wrapped in both modes. **Never executes the query** — uses `SET SHOWPLAN_XML ON`.

Implementation note: `SET SHOWPLAN_XML ON` is session-scoped. Verify `Connection Reset=true` in Microsoft.Data.SqlClient clears it after the connection returns to the pool, or run on a `pooling=false` connection to prevent post-`explain_query` calls from silently returning plans instead of results.

`format: "summary"` is the default because raw plan XML for complex queries can be multi-MB; the byte cap would truncate it producing invalid XML. Summary extracts the actionable insights an Agent needs.

## Ops tools

All three query `sys.dm_*` DMVs. All accept optional `database`.

### `analyze_indexes(database?, query?)`

| Param | Type | Default | Notes |
|---|---|---|---|
| `database` | string? | current | Cross-DB-validated |
| `query` | string? | none | If provided, per-query missing-index analysis (filters to a plan_handle); if omitted, workload-wide analysis |

Merges what were originally two separate tools (`analyze_query_indexes` + `analyze_workload_indexes`). Both query `sys.dm_db_missing_index_*` DMVs; the `query` param just adds a JOIN/filter to a plan_handle. One tool, two clear behaviors.

### `get_top_queries(database?, order_by?, limit?)`

| Param | Type | Default | Max | Notes |
|---|---|---|---|---|
| `database` | string? | current | — | Cross-DB-validated |
| `order_by` | enum? | `"avg_cpu"` | — | `"avg_cpu"`, `"total_cpu"`, `"avg_duration"`, `"total_duration"`, `"total_logical_reads"`, `"execution_count"` |
| `limit` | int? | 10 | 100 | |

Uses `sys.dm_exec_query_stats` joined to `sys.dm_exec_sql_text` filtered by `dbid`. Works without Query Store.

### `analyze_db_health(database?)`

| Param | Type | Default | Notes |
|---|---|---|---|
| `database` | string? | current | Cross-DB-validated |

Returns summary-level health checks, not raw DMV rows. The Agent drills down with `execute_sql` if a summary flag warrants. Runs 5 separate queries:

1. Database size + log size
2. VLF count (high count = slow recovery)
3. Index fragmentation summary (`sys.dm_db_index_physical_stats` with `SAMPLED` mode — not `DETAILED`)
4. Statistics staleness
5. Active blocking (zero rows if none)

Return shape: array of summary objects, e.g. `[{"check": "index_fragmentation", "total_indexes": 200, "fragmented_gt_30pct": 15, "worst": "dbo.Orders (87%)"}, ...]`

## Cross-database query safety

Every tool accepting a `database` parameter must validate it via three checks against `sys.databases`:

1. **Exists**: database with that name exists
2. **Online**: `state_desc = 'ONLINE'` (catches RESTORING/OFFLINE/EMERGENCY/SUSPECT)
3. **Multi-user**: `user_access_desc = 'MULTI_USER'` (catches SINGLE_USER/RESTRICTED_USER)

If any check fails, return a `CONNECTION` error with a specific `detail` naming which check failed.

Database name is injected into queries via bracketed identifier (`[{db}].sys.objects`), NOT string concatenation into the SQL. `QuoteIdentifier` must double internal brackets: `[my]db]` → `[my]]db]]`. This is load-bearing — write a unit test for `[my]weird]db]`.

DB snapshots work (they're read-only). Linked-server four-part names are already blocked by the Guard (ADR-0006 Layer 2).

## Oracle's 3 watch-out-fors (implementation-phase verification)

1. **`QuoteIdentifier` correctness is load-bearing** — write unit test for `[my]weird]db]`.
2. **Verify `Connection Reset=true` actually clears `SHOWPLAN_XML` state** — if not, queries after `explain_query` silently return plans instead of results.
3. **Agent context window is the real constraint, not the 10MB byte cap** — any tool returning unbounded rows should have a default `limit` (only `list_objects` does in v1; `get_top_queries` has `limit` default 10).

## Tool annotations (MCP SDK)

Per ADR-0008, the C# MCP SDK defaults `Destructive=true` per the MCP spec. All Restricted-mode tools MUST explicitly set `[McpServerTool(ReadOnly=true, Destructive=false)]`. `execute_sql` in Unrestricted mode sets `[McpServerTool(Destructive=true)]` explicitly.
