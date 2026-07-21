# No application-layer row cap; transport-safety byte limit with notice

`execute_sql` and `explain_query` return everything SqlClient gives us — **no row limit**. This deliberately mirrors postgres-mcp's behavior because the user judges agent-managed SQL (`TOP N`, `OFFSET/FETCH`) to be sufficient for controlling result size, and prefers a clean "return what SQL Server returns" contract over an artificial row cap that silently drops rows.

However, an unbounded `SELECT *` against a large table will produce a payload that exceeds MCP transport limits — stdio JSON-RPC fails in the tens-of-MB range, and the agent receives a transport-level broken-pipe error, not a clean signal. The agent then retries the same query and hits the same wall.

**Decision**: No row cap, but a **hard byte-size transport safety net** that truncates with notice before transport death. The server serializes rows into the result buffer; when the buffer crosses the configured byte threshold, the server stops reading, returns the rows accumulated so far, and appends a second `TextContent` item:

```
[truncated] Result exceeded {N} bytes. {M} rows returned, more exist. Narrow with WHERE, TOP, or OFFSET/FETCH.
```

The agent sees data + truncation signal in the same `CallToolResult`, and can recover by refining the query.

**Defaults & configuration**:
- Default threshold: **10 MB** (`10485760` bytes) — the stdio sweet spot before most MCP hosts choke.
- Env var: `MSSQL_MAX_RESULT_BYTES` — overrides the default. Set to `0` to disable the safety net entirely (restores pure no-cap behavior; transport death is then the user's problem).
- No CLI flag — this is a runtime tunable, not a startup-shape decision.

**Consequences**:
- The Guard's command timeout (ADR-0007) remains the temporal protection; the byte limit is the spatial protection.
- This is a refinement of the original no-cap decision, not a reversal. Row semantics are still fully controlled by the query.
- The reversal path is narrowing: lower the default or set `MSSQL_MAX_RESULT_BYTES=0`.
