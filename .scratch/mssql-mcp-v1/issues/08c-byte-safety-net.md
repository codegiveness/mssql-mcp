# 08c — Byte-size safety net (10MB truncation)

**What to build:** A hard byte-size transport safety net that prevents large result sets from killing the MCP stdio transport. The server serializes rows into the result buffer; when the buffer crosses the configured byte threshold, the server stops reading, returns the rows accumulated so far, and appends a second `TextContent` item with a truncation notice. Default threshold 10MB (`10485760` bytes), overridable via `MSSQL_MAX_RESULT_BYTES` env var, `0` disables entirely. This protects the transport (stdio JSON-RPC fails in the tens-of-MB range) without limiting query semantics — the Agent can always narrow with `WHERE`/`TOP`/`OFFSET/FETCH`.

**Blocked by:** 03 (`execute_sql` in Restricted mode — needs a working tool to attach the safety net to)

**Status:** ready-for-agent

- [ ] Byte counter tracks serialized size as rows are added to the result buffer
- [ ] When `MSSQL_MAX_RESULT_BYTES` threshold is reached: stop reading from `SqlDataReader`, finalize the JSON array, append second `TextContent` item
- [ ] Truncation notice text: `[truncated] Result exceeded {N} bytes. {M} rows returned, more exist. Narrow with WHERE, TOP, or OFFSET/FETCH.`
- [ ] Default threshold: `10485760` (10 MB)
- [ ] `MSSQL_MAX_RESULT_BYTES=0` disables the safety net entirely (pure no-cap behavior)
- [ ] Invalid `MSSQL_MAX_RESULT_BYTES` value (negative, non-numeric) fails fast at startup
- [ ] Applies to `execute_sql`, `explain_query` (xml format), and all discovery/ops tools that return rowsets
- [ ] Unit test: fake `ISqlConnection` returns 1000 rows of 100KB each; verify truncation at 10MB threshold with correct notice
- [ ] Unit test: `MSSQL_MAX_RESULT_BYTES=0` returns all rows without truncation
- [ ] Unit test: `MSSQL_MAX_RESULT_BYTES=1048576` (1MB) truncates earlier
- [ ] Unit test: truncation notice is the SECOND `TextContent` item (data is first)
