# 06 — Ops tools

**What to build:** Three operations tools that query `sys.dm_*` DMVs. `analyze_indexes(database?, query?)` returns missing index recommendations from `sys.dm_db_missing_index_*` — per-query analysis when `query` is provided (filtered to a plan handle), workload-wide when omitted. `get_top_queries(database?, order_by?, limit?)` returns the most expensive queries by CPU/duration/reads using `sys.dm_exec_query_stats` joined to `sys.dm_exec_sql_text` filtered by `dbid`. `analyze_db_health(database?)` returns summary-level health checks: database size, VLF count, index fragmentation (SAMPLED mode, not DETAILED), statistics staleness, active blocking. Returns summary objects, not raw DMV rows — the Agent drills down with `execute_sql` if a summary flag warrants.

**Blocked by:** 04 (Cross-DB safety + remaining discovery tools — needs `QuoteIdentifier` and `ValidateDatabase`)

**Status:** ready-for-agent

- [ ] `analyze_indexes(database?, query?)` tool: queries `sys.dm_db_missing_index_details` joined to `sys.dm_db_missing_index_group_stats` and `sys.dm_db_missing_index_groups`; `query` param filters to a plan handle
- [ ] Returns: index name, equality/inequality/included columns, estimated improvement, user seeks, avg user impact
- [ ] `get_top_queries(database?, order_by?, limit?)` tool: queries `sys.dm_exec_query_stats` joined to `sys.dm_exec_sql_text` cross-applied, filtered by `dbid`
- [ ] `order_by` enum: `"avg_cpu"` (default), `"total_cpu"`, `"avg_duration"`, `"total_duration"`, `"total_logical_reads"`, `"execution_count"`
- [ ] `limit` default 10, max 100
- [ ] Returns: query text (truncated to first 500 chars), the metric value, execution count, plan generation number
- [ ] `analyze_db_health(database?)` tool: runs 5 separate queries and returns summary objects
- [ ] Summary shape: `[{"check":"database_size","size_mb":1234,"log_mb":56}, {"check":"vlf_count","count":12,"status":"ok"}, {"check":"index_fragmentation","total_indexes":200,"fragmented_gt_30pct":15,"worst":"dbo.Orders (87%)"}, {"check":"stats_staleness","stale_gt_7d":5,"oldest":"dbo.Users (30d)"}, {"check":"blocking","blocked_sessions":0}]`
- [ ] Index fragmentation uses `sys.dm_db_index_physical_stats` with `SAMPLED` mode (NOT `DETAILED` — too slow for large tables)
- [ ] All 3 tools registered with `[McpServerTool(ReadOnly=true, Destructive=false)]`
- [ ] Unit tests (fake `ISqlConnection` with canned DMV results)
- [ ] Integration tests against real DB
