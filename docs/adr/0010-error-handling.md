# Error handling: tiered by error class

Tool execution errors surface as `CallToolResult` with `isError: true` and `TextContent` whose text is structured JSON (or plain text for human-readable classes). Every error carries an `error` discriminator field so agents can branch programmatically. SQL errors additionally carry `code`/`severity` for recovery logic.

## Error classes and shapes

All errors follow a consistent JSON envelope: `{"error": "<DISCRIMINATOR>", ...details}`. This replaces the earlier bracketed-prefix convention — agents now parse a single structured shape rather than regex-matching prefixes.

| Class | Discriminator | Trigger | Shape |
|---|---|---|---|
| Guard | `"GUARD_REJECTION"` | AST rejection (non-SELECT/WITH statement, SELECT INTO, OPENROWSET, etc.), AST Layer-2 intra-SELECT rejection, empty batch, parse error | `{"error": "GUARD_REJECTION", "rule": "non_select_statement", "detail": "Statement type 'DeleteStatement' is not allowed in Restricted mode", "statement_type": "DELETE", "position": {"line": 1, "column": 1}}` |
| Timeout | `"TIMEOUT"` | `OperationCanceledException` from command timeout | `{"error": "TIMEOUT", "timeout_ms": 30000, "detail": "Query exceeded 30s command timeout"}` |
| Connection | `"CONNECTION"` | `SqlException` classified as transient after retries exhausted, or `InvalidOperationException` on broken pool | `{"error": "CONNECTION", "detail": "{message}. Retries exhausted."}` |
| SQL | `"SQL"` | Non-transient `SqlException` | `{"error": "SQL", "code": "SQL208", "message": "Invalid object name 'users'.", "severity": 16, "line": 1, "procedure": null}` |
| Internal | `"INTERNAL"` | Any other unhandled exception | `{"error": "INTERNAL", "exception_type": "InvalidOperationException", "detail": "{Message}"}` — never stack traces |
| Object not found | `"OBJECT_NOT_FOUND"` | `get_object_details` returns zero rows | `{"error": "OBJECT_NOT_FOUND", "schema": "dbo", "name": "Orders", "type": "TABLE", "database": "SalesDB"}` |

All six classes return `isError: true`. Agents branch on the `error` discriminator field; `SQL` errors additionally let agents parse `code`/`severity` to decide recovery (≤10 warning, 11-16 fixable SQL error, 17-25 server/resource).

## Severity 25 (fatal SQL Server errors)

Surface the error, do **not** exit the process. The agent ran a query that hit a fatal server state — it didn't cause one. Process exit would kill the MCP server for all subsequent calls. Process exit is reserved for `--validate` failures and startup config errors (handled before the MCP loop starts), not tool runtime.

## Considered Options

- **C. Tiered by error class** ✅ — chosen
- A. Plain text only (postgres-mcp style) — rejected: loses SQL severity/code the agent uses to self-correct
- B. Structured JSON for every error (DAB style) — rejected: overkill for `[timeout]` and `[connection]`; adds tokens without agent value
- D. Always structured JSON envelope — rejected: same problem as B, plus inconsistent with the `[guard]`/`[timeout]` prefix convention which is already structured by prefix

## Consequences

- Stack traces never reach the Agent. Full detail (including stack) goes to logs at `Debug`/`Error` level — see logging decision (ADR-0011).
- Guard rejections carry a `rule` field naming the rejecting rule (e.g. `non_select_statement`, `select_into`, `openrowset`, `parse_error`, `empty_batch`) so agents can self-correct precisely.
- Empty result set is NOT an error for rowset tools — returns `[]` per ADR-0009. Exception: `get_object_details` returns `OBJECT_NOT_FOUND` on zero rows, because object lookup is never ambiguous (if it's not there, it's an error).
- Severity-25 errors are surfaced to the agent, not fatal to the process.
- Transient errors only reach `CONNECTION` after `SqlRetryLogicOption` (ADR-0004) exhausts retries — our code never sees the first-attempt transient error.
