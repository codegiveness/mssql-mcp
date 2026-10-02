# No application-layer row cap; transport-safety byte limit with notice

## Context

`execute_sql` has **no row-count limit**. Agent-managed SQL (`TOP N`, `OFFSET/FETCH`) controls row counts. A positive byte budget may stop a result early with an explicit notice; raw `explain_query` XML is refused whole if oversized, never cut into invalid XML.

However, an unbounded `SELECT *` against a large table will produce a payload that exceeds MCP transport limits — stdio JSON-RPC fails in the tens-of-MB range, and the agent receives a transport-level broken-pipe error, not a clean signal. The agent then retries the same query and hits the same wall.

## Decision

No row cap. A positive `MSSQL_MAX_RESULT_BYTES` applies two protections: the executor stops retaining rows before their conservative JSON byte budget exceeds the limit, and the tools enforce a precise UTF-8 limit on the final serialized data array. The executor returns `SqlQueryResult(Rows, IsTruncated)` so early termination remains visible even when retained rows or transformed results serialize below the threshold. The response appends a second `TextContent` item:

```
[truncated] Result exceeded {N} bytes. {M} rows returned, more exist. Narrow with WHERE, TOP, or OFFSET/FETCH.
```

The agent sees data + truncation signal in the same `CallToolResult`, and can recover by refining the query.

The allocation-free reader accounting includes array/object delimiters, commas, keys, colons, null literals, and every supported coerced value. Strings include quotes and conservative JSON escaping/Unicode bounds; numeric types use fixed maximum encoded widths. Saturating `long` arithmetic prevents overflow. It is deliberately conservative, not a `ToString().Length * 2` estimate or repeated serialization. It can stop before the exact serialized array would fill the limit. Both passes reserve the closing array bracket.

Finite internal database/object existence lookups explicitly disable the budget so a tiny cap cannot manufacture a not-found result. User-facing metadata/detail queries are bounded, including each query in grouped tools; their truncation flags are combined for the final response. Health checks omit a summary whose input was cut short rather than report an invented zero/partial aggregate. Transformed rows retain the original truncation signal.

Raw SHOWPLAN uses sequential XML/text readers and a bounded chunked accumulator. A positive limit refuses an oversized plan with `PLAN_TOO_LARGE` before constructing the full output string; it never falls back to full materialization for an unknown size. Summary format passes zero for the underlying plan and is unchanged. `SET SHOWPLAN_XML OFF` runs even on refusal, cancellation or errors, ignoring request cancellation; failed cleanup discards the connection pool.

This is not a strict process-memory or JSON-RPC-envelope bound: an individual SQL LOB/coerced row or XML parser token can still allocate before accounting, and summary XML is not bounded. Multi-query tools retain a finite number of bounded query results, not one shared reader budget. JSON-RPC wrapping and the notice are outside the data budget. An empty valid JSON array requires two bytes even when the configured threshold is one.

**Defaults & configuration**:
- Default threshold: **10 MB** (`10485760` bytes) — the stdio sweet spot before most MCP hosts choke.
- Env var: `MSSQL_MAX_RESULT_BYTES` — overrides the default. Set to `0` to disable the safety net entirely (restores pure no-cap behavior; transport death is then the user's problem).
- No CLI flag — this is a runtime tunable, not a startup-shape decision.

## Considered Options

No alternatives were seriously considered for this decision — the byte-size truncation with notice is the only approach that preserves the "return what SQL Server returns" contract while preventing transport death.

## Consequences

- The Guard's command timeout (ADR-0007) remains the temporal protection; the byte limit prevents unbounded row accumulation and oversized data transport, subject to the allocation limits above.
- This is a refinement of the original no-cap decision, not a reversal. Row semantics are still fully controlled by the query.
- The reversal path is narrowing: lower the default or set `MSSQL_MAX_RESULT_BYTES=0`.
