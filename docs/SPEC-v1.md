# Spec: mssql-mcp v1 — MCP Server for Microsoft SQL Server

## Problem Statement

AI agents (Claude Desktop, Cursor, Windsurf) can't safely query Microsoft SQL Server through the Model Context Protocol. The existing options are broken or incomplete:

- **c0h1b4/mssql-mcp-server** — the most-starred MSSQL MCP server — exposes only a single `query` tool that runs raw SQL with no parameterization, no read-only mode, no row limits, no timeout, no reconnect logic, and a broken connection-string parser. Its README claims `list_databases`, `list_tables`, `describe_table` tools that don't exist in the code. It was never published to npm (the package name is owned by a different project). Last commit was months ago with open issues unanswered.
- **Microsoft's own DAB MCP** — a feature inside Data API Builder — requires a DAB config file, only exposes single-entity CRUD (no ad-hoc SQL, no JOINs, no DDL), and returns empty `properties: {}` input schemas for custom tools, forcing agents to guess parameters. Not standalone.
- **postgres-mcp** (by crystaldba) — the functional gold standard — exists only for PostgreSQL. MSSQL has no equivalent.

A developer who wants to point Claude Desktop at a SQL Server database has no good option. They need: safe read-only access by default, structured discovery tools, execution-plan analysis, workload diagnostics, dual-mode for when writes are needed, and one-command installation via npm or dotnet tool — all using the official Microsoft ADO.NET driver.

## Solution

**mssql-mcp** — a standalone MCP server for Microsoft SQL Server, built in C#/.NET 10 with `Microsoft.Data.SqlClient` and `Microsoft.SqlServer.TransactSql.ScriptDom`. Ships read-only by default (Restricted mode) with an opt-in write mode (Unrestricted). Installs via `npx mssql-mcp` or `dotnet tool install mssql-mcp`. Exposes 9 tools: 4 discovery, 2 SQL, 3 ops.

From the user's perspective: add a connection string to their MCP client config, and the Agent can immediately explore the schema, run safe SELECT queries, analyze query plans, and diagnose workload health — all gated by a multi-layer Guard that prevents destructive SQL in Restricted mode. When the user explicitly opts into Unrestricted mode, DML/DDL is permitted with clear `destructiveHint` signaling.

## User Stories

### Installation & setup

1. As a Claude Desktop user, I want to install mssql-mcp via `npx -y mssql-mcp` with no prerequisites, so that I can start querying SQL Server immediately.
2. As a .NET developer, I want to install mssql-mcp via `dotnet tool install -g mssql-mcp`, so that I can use it with the .NET tooling I already have.
3. As a Windows user, I want the installation to work on Windows, so that I can use mssql-mcp on my primary OS (even if it requires the .NET runtime as a dependency).
4. As a Linux user, I want a self-contained binary, so that I don't need to install the .NET runtime separately.
5. As an macOS user (both Intel and Apple Silicon), I want a self-contained binary, so that I can use mssql-mcp without a separate runtime install.
6. As a user behind a corporate proxy, I want the npm postinstall script to fail clearly when the binary download is blocked, so that I know to use the `dotnet tool` fallback instead of getting a silent broken install.
7. As a user on an unsupported platform (e.g. FreeBSD), I want the install script to tell me which platforms are supported and point me to the dotnet tool fallback, so that I'm not stuck guessing.
8. As a user installing on an air-gapped machine, I want the install to fail with a clear error message naming the `dotnet tool install` alternative, so that I have a path forward.

### Connection & authentication

9. As a developer with a local SQL Server, I want to connect via SQL password authentication, so that I can query my development database.
10. As a Windows developer with a domain-joined SQL Server, I want to connect via Windows Integrated Authentication, so that I don't need to put passwords in my config.
11. As an Azure user with a Managed Identity, I want to connect via Active Directory Default authentication, so that I can use passwordless auth to Azure SQL.
12. As a user, I want to provide the connection string via an environment variable, so that secrets don't appear in my process arguments or MCP client config JSON.
13. As a user, I want to provide the connection string via a CLI flag, so that I can test quickly from the command line.
14. As a user, I want the environment variable to take precedence over the CLI flag, so that containerized deployments work correctly with secret managers.
15. As a user, I want a `--validate` flag that opens and closes the connection and exits, so that I can verify my connection string before starting the server.
16. As a user with a transient network issue, I want the server to retry the connection automatically using Microsoft's transient-error detection, so that momentary blips don't surface as errors to the Agent.
17. As a user, I want the retry count and interval to be configurable via environment variables, so that I can tune for my network conditions.
18. As a user, I want my password obfuscated in logs, so that credentials don't leak to log aggregators.

### Restricted mode (default — read-only)

19. As a user, I want the server to default to Restricted mode, so that I can safely point it at a production database without fear of data corruption.
20. As a user, I want every SQL statement in Restricted mode validated against an AST allowlist, so that only SELECT and WITH...SELECT statements execute.
21. As a user, I want multi-statement injection (`SELECT 1; DROP TABLE x`) rejected, so that an Agent can't accidentally destroy data by chaining statements.
22. As a user, I want `GO`-separated batches (`SELECT 1 GO DROP TABLE x`) rejected if any batch contains non-SELECT, so that the batch separator can't be used to bypass the Guard.
23. As a user, I want nested statements inside `BEGIN...END`, `IF`, and `WHILE` blocks inspected, so that destructive SQL hidden inside control-of-flow statements is caught.
24. As a user, I want `SELECT ... INTO` rejected in Restricted mode, so that DDL disguised as SELECT can't create tables.
25. As a user, I want `OPENROWSET`, `OPENDATASOURCE`, `OPENQUERY`, `OPENXML` rejected, so that the Agent can't read arbitrary files or access remote servers.
26. As a user, I want `EXECUTE AS` rejected, so that the Agent can't escalate privileges.
27. As a user, I want four-part (linked-server) names rejected, so that the Agent can't access linked servers without explicit configuration.
28. As a user, I want every Restricted-mode query wrapped in `BEGIN TRANSACTION ... ROLLBACK TRANSACTION`, so that even if the AST allowlist misses something, the transaction rollback is a backstop.
29. As a user, I want a default 30-second query timeout in Restricted mode, so that a runaway query doesn't hang the Agent indefinitely.
30. As a user, I want the timeout configurable via CLI flag and environment variable, so that I can tune it for long-running analytical queries.
31. As a user, I want every query prefixed with a `/* mssql-mcp */` sentinel comment, so that a DBA can identify mssql-mcp queries in `sys.dm_exec_requests` and `sys.dm_exec_sql_text`.

### Unrestricted mode (opt-in — read-write)

32. As a developer, I want to opt into Unrestricted mode via `--access-mode unrestricted`, so that I can run DML and DDL when I intentionally need to.
33. As a user, I want Unrestricted mode tools to carry `destructiveHint=true`, so that the MCP client can warn me before executing destructive operations.
34. As a user, I want DML statements to return a status object with `rows_affected`, so that I know how many rows were modified.
35. As a user, I want DDL statements to return a status object naming the created/dropped object, so that I can verify the operation succeeded.

### Discovery tools

36. As an Agent, I want a `list_databases` tool that returns all databases with an `is_current` flag, so that I can orient myself on the first call and know which database I'm connected to.
37. As an Agent, I want a `list_schemas` tool, so that I can enumerate schemas in the current or a specified database.
38. As an Agent, I want a `list_objects` tool with schema, type, and limit filters, so that I can explore database objects without being overwhelmed by thousands of rows.
39. As an Agent, I want Microsoft-shipped objects filtered out by default, so that system tables and stored procedures don't clutter my results.
40. As an Agent, I want a `limit` parameter on `list_objects` defaulting to 1000, so that my context window isn't saturated by a database with 50,000 objects.
41. As an Agent, I want a truncation notice when `list_objects` hits the limit, so that I know to refine my filters or raise the limit.
42. As an Agent, I want a `get_object_details` tool that returns columns, parameters, indexes, and triggers for a specific object, so that I can understand an object's structure without writing catalog queries myself.
43. As an Agent, I want `get_object_details` to return an explicit `OBJECT_NOT_FOUND` error on zero rows, so that I'm not confused by an empty array.

### SQL tools

44. As an Agent, I want an `execute_sql` tool that runs a T-SQL SELECT in Restricted mode, so that I can query data.
45. As an Agent, I want the Guard to reject malformed SQL with a parse error naming the line and column, so that I can fix syntax errors.
46. As an Agent, I want Guard rejections to carry a `rule` field naming the specific rule violated, so that I can self-correct precisely (e.g. "non_select_statement" vs "select_into").
47. As an Agent, I want an `explain_query` tool that returns a summary execution plan (cost, missing indexes, warnings, top operations), so that I can understand query performance without executing it.
48. As an Agent, I want an `explain_query` tool with a `format: "xml"` option for raw SHOWPLAN_XML, so that I can inspect the full plan when the summary isn't enough.
49. As a user, I want `explain_query` to never execute the query, so that even in Unrestricted mode, a plan analysis can't have side effects.
50. As a user, I want `explain_query` to validate the SQL with the Guard even in Unrestricted mode, so that there's no legitimate reason to bypass plan analysis.

### Ops tools

51. As a DBA, I want an `analyze_indexes` tool that shows missing indexes from the query workload, so that I can improve query performance.
52. As a DBA, I want `analyze_indexes` to accept an optional `query` parameter, so that I can get per-query missing-index analysis instead of workload-wide.
53. As a DBA, I want a `get_top_queries` tool ordered by CPU/duration/reads, so that I can identify the most expensive queries.
54. As a DBA, I want an `analyze_db_health` tool that returns summary-level health checks (size, VLFs, fragmentation, stats, blocking), so that I can assess database health without running 5 separate DMV queries.
55. As a DBA, I want `analyze_db_health` to use `SAMPLED` mode for index physical stats, so that the check doesn't scan every page (DETAILED mode is too slow for large tables).

### Result handling

56. As an Agent, I want query results returned as a lean JSON array of objects, so that I can parse them directly.
57. As an Agent, I want `bigint` and `decimal` values returned as strings, so that I don't lose precision beyond 2^53.
58. As an Agent, I want binary values returned as base64 strings, so that I can transport them safely in JSON.
59. As an Agent, I want date/time values returned as ISO 8601 strings, so that I can parse them unambiguously.
60. As a user, I want a 10 MB byte-size safety net that truncates results with a notice, so that a large result set doesn't kill the MCP transport (stdio JSON-RPC) with a broken pipe.
61. As a user, I want the byte-size safety net configurable via `MSSQL_MAX_RESULT_BYTES`, so that I can tune it for my MCP client's capacity.
62. As a user, I want to disable the byte-size safety net entirely by setting `MSSQL_MAX_RESULT_BYTES=0`, so that I can get raw unbounded results if my transport supports it.
63. As an Agent, I want the truncation notice appended as a second `TextContent` item, so that I see both the data and the truncation signal in one `CallToolResult`.

### Error handling

64. As an Agent, I want errors returned as structured JSON with a discriminator field, so that I can branch programmatically on error type.
65. As an Agent, I want SQL errors to include `code`, `severity`, `line`, and `procedure`, so that I can decide recovery (≤10 warning, 11-16 fixable, 17-25 server/resource).
66. As an Agent, I want timeout errors to include the timeout value in milliseconds, so that I know the limit I hit.
67. As an Agent, I want connection errors to indicate "retries exhausted", so that I know the server tried multiple times before failing.
68. As an Agent, I want internal errors to include the exception type but never the stack trace, so that I get enough to report but not a security-leaking dump.
69. As a user, I want severity-25 (fatal) SQL errors surfaced to the Agent rather than killing the process, so that one bad query doesn't take down the MCP server for all subsequent calls.
70. As an Agent, I want empty result sets to return `[]` (not an error), so that "no rows matched" is distinct from "query failed."

### Cross-database access

71. As an Agent, I want an optional `database` parameter on discovery and ops tools, so that I can query a database other than the one in the connection string.
72. As a user, I want the `database` parameter validated for existence, online state, and multi-user access, so that the Agent doesn't get cryptic errors from RESTORING or SINGLE_USER databases.
73. As a user, I want database names properly bracketed with internal `]` doubled, so that a database named `my]db` doesn't break the query or enable injection.

### Logging & observability

74. As a user, I want logs to stderr (never stdout), so that the MCP JSON-RPC channel on stdout isn't corrupted.
75. As a user, I want an optional log file via `MSSQL_LOG_FILE`, so that I can persist logs for debugging.
76. As a user, I want a `--log-level` flag and `MSSQL_LOG_LEVEL` env var, so that I can control log verbosity.
77. As a DBA, I want the `/* mssql-mcp */` sentinel comment on every query, so that I can filter mssql-mcp traffic in SQL Server traces and Extended Events.

### Distribution & release

78. As a maintainer, I want `git tag v0.1.0 && git push --tags` to be the complete release command, so that releasing is a one-step operation.
79. As a maintainer, I want CI to run on every push to main and on PRs, so that regressions are caught between releases.
80. As a maintainer, I want the release pipeline to build self-contained binaries for linux-x64, linux-arm64, osx-x64, osx-arm64, and framework-dependent for win-x64, so that all major platforms are covered.
81. As a maintainer, I want SHA256 checksums published with each release, so that the npm postinstall script can verify download integrity.
82. As a maintainer, I want the npm package version synced from the git tag, so that there's one source of truth for versioning.
83. As a maintainer, I want the tool surface to be stable from `0.1.0` (no breaking tool schema changes within `0.x`), so that MCP clients don't break between minor versions.
84. As a maintainer, I want a clear set of graduation triggers for `1.0.0` (production usage, guard audit, stable surface, verified distribution, test coverage), so that I know when the project is truly stable.
85. As a maintainer, I want release candidates (`1.0.0-rc.1`) before stable releases, so that I can catch issues before committing to `1.0.0`.

## Implementation Decisions

### Architecture

- **3-project solution** (`mssql-mcp.sln`): `mssql-mcp.Core` (Guard, SqlExecutor, Options — no MCP deps; depends on `Microsoft.Data.SqlClient` + `Microsoft.SqlServer.TransactSql.ScriptDom` + `Microsoft.Extensions.Options`), `mssql-mcp.Tools` (9 tool classes with `[McpServerTool]` attributes; depends on Core + `ModelContextProtocol` SDK), `mssql-mcp` (App: Program.cs, DI, stdio transport, CLI, npm entrypoint; depends on Tools). The dependency graph is `Core ← Tools ← App` — cross-project references enforce wiring at compile time, preventing dead code patterns.
- **MCP SDK**: Official `ModelContextProtocol` NuGet package, pinned to v1.4.1 stable. stdio transport via `.WithStdioServerTransport()`. Tool registration via `[McpServerToolType]`/`[McpServerTool]` attributes. Critical: the SDK defaults `Destructive=true` per MCP spec — Restricted-mode tools MUST explicitly set `[McpServerTool(ReadOnly=true, Destructive=false)]`.
- **Transport**: stdio only for v1. Every MCP host invokes via stdio. HTTP transports (SSE, streamable-http) deferred to v2 (additive, non-breaking).

### Guard (Restricted mode safety)

- **Parsing entry point**: `TSql160Parser.Parse()` (NOT `ParseStatementList()`). Returns `TSqlScript` with a `Batches` collection. `GO` creates new batches; `;` separates statements within a batch.
- **Layer 1 — Statement-type allowlist via Visitor**: A `TSqlFragmentVisitor` overrides `Visit(SelectStatement)` to whitelist it. A catch-all override records every concrete statement type encountered anywhere in the AST — including nested inside `BeginEndBlockStatement`, `IfStatement`, `WhileStatement`. If the recorded set contains anything other than `SelectStatement`, reject. This catches: multi-statement batches (`SELECT 1; DROP TABLE x`), GO-separated batches (`SELECT 1 GO DROP TABLE x`), and nested statements (`BEGIN DROP TABLE x END`).
- **Layer 1 edge cases**: Reject `TSqlStatementSnippet` (partial parse failure). Reject empty batches (0 statements = comment-only input). Reject parse errors with line/column.
- **Layer 2 — Targeted intra-statement blocklist**: Reject `SELECT ... INTO` (checks `node.Into`), `OPENROWSET`/`OPENDATASOURCE`/`OPENQUERY`/`OPENXML` in FROM, `EXECUTE AS`, four-part (linked-server) names, `BulkInsertStatement`.
- **Execution backstop**: Every Restricted-mode query wrapped in `BEGIN TRANSACTION ... ROLLBACK TRANSACTION` even after AST allowlist passes. Isolation level: READ COMMITTED (default). This is the backstop for AST misses.
- **`explain_query` is Guarded in BOTH modes** — Unrestricted skips the guardrail for `execute_sql` but never for `explain_query`. `explain_query` uses `SET SHOWPLAN_XML ON` and never executes the query. Implementation must verify `Connection Reset=true` clears `SHOWPLAN_XML` session state.

### Connection lifecycle

- Single connection string fixed at startup (env var `MSSQL_CONNECTION_STRING` takes precedence over CLI `--connection-string`). No per-call connection strings.
- SqlClient built-in connection pool handles pooling. No custom pool.
- Transient failure retry via `SqlRetryLogicOption` (Microsoft maintains the transient-error list — do NOT write a custom retry layer). Defaults: retry 3, backoff 2-10s, overridable via `MSSQL_RETRY_COUNT`/`MSSQL_RETRY_INTERVAL`.

### Authentication

- SQL password (universal)
- Windows Integrated (`Integrated Security=SSPI`, Windows-only via dotnet-tool/framework-dependent — SNI license blocks self-contained Windows)
- Active Directory Default (`Authentication=Active Directory Default` — covers MSI/VS/CLI, what DAB uses)
- AD Password and AD Service Principal deferred to v1.1.

### Tool surface (9 tools)

Discovery:
- `list_databases()` — returns databases with `is_current` flag, excludes system DBs and `mssqlsystemresource`.
- `list_schemas(database?)` — all schemas including system, sorted by `schema_id`.
- `list_objects(database?, schema?, type?, limit?)` — `is_ms_shipped=0` filter, `limit` default 1000 max 5000, `type` enum maps to `sys.objects.type` char codes (`"TABLE"`→`'U'`, `"VIEW"`→`'V'`, `"PROCEDURE"`→`('P','PC')`, `"FUNCTION"`→`('FN','IF','TF','FS','FT')`). Truncation notice prepended when limit hit.
- `get_object_details(database?, schema, name, type?)` — returns columns/parameters/indexes/triggers. Returns `OBJECT_NOT_FOUND` error on zero rows.

SQL:
- `execute_sql(sql)` — single T-SQL string, no `parameters[]` array. Guard validates in Restricted. Returns non-rowset status objects in Unrestricted (`[{"result":"success","statement_type":"UPDATE","rows_affected":42}]`).
- `explain_query(sql, format?)` — `format` default `"summary"` (cost, missing indexes, warnings, top 5 ops), or `"xml"` for raw SHOWPLAN_XML. Guard validates in both modes. Never executes.

Ops:
- `analyze_indexes(database?, query?)` — missing index analysis from `sys.dm_db_missing_index_*` DMVs. `query` provided = per-query; omitted = workload-wide.
- `get_top_queries(database?, order_by?, limit?)` — `sys.dm_exec_query_stats` joined to `sys.dm_exec_sql_text`, filtered by `dbid`. `order_by` enum: `avg_cpu`, `total_cpu`, `avg_duration`, `total_duration`, `total_logical_reads`, `execution_count`. `limit` default 10 max 100.
- `analyze_db_health(database?)` — summary-level: size, VLFs, fragmentation (SAMPLED mode), stats staleness, blocking. Returns summary objects, not raw DMV rows.

### Return shape

- Lean JSON array of objects as `TextContent`. No schema envelope, no columnar layout, no markdown.
- Type coercion: `int`/`smallint`/`tinyint`/`bit`→number, `bigint`/`decimal`/`numeric`/`money`/`smallmoney`→string (precision), `real`/`float`→number, dates→ISO 8601 string, `uniqueidentifier`→string, binary→base64, char/varchar/nchar/nvarchar/text/ntext→string, `geography`/`geometry`/`hierarchyid`/`xml`→string (`.ToString()`), NULL→null.
- Empty result→`[]`. Single row still array.
- Non-rowset (Unrestricted DDL/DML)→status objects.
- Byte-size safety net: 10 MB default, truncates with notice, `MSSQL_MAX_RESULT_BYTES` overridable, `0` disables.

### Error shape

Structured JSON envelope with `error` discriminator field:
- `{"error": "GUARD_REJECTION", "rule": "non_select_statement", "detail": "...", "statement_type": "DELETE", "position": {"line": 1, "column": 1}}`
- `{"error": "TIMEOUT", "timeout_ms": 30000, "detail": "..."}`
- `{"error": "CONNECTION", "detail": "..."}`
- `{"error": "SQL", "code": "SQL208", "message": "...", "severity": 16, "line": 1, "procedure": null}`
- `{"error": "INTERNAL", "exception_type": "...", "detail": "..."}`
- `{"error": "OBJECT_NOT_FOUND", "schema": "...", "name": "...", "type": "...", "database": "..."}`

All return `isError: true`. Stack traces never reach the Agent (go to logs at Debug/Error level).

### Cross-database safety

Every tool accepting `database` validates via three checks against `sys.databases`:
1. Exists
2. `state_desc = 'ONLINE'` (catches RESTORING/OFFLINE/EMERGENCY)
3. `user_access_desc = 'MULTI_USER'` (catches SINGLE_USER/RESTRICTED_USER)

Database name injected via bracketed identifier (`[{db}].sys.objects`), NOT string concatenation. `QuoteIdentifier` doubles internal brackets: `[my]db]` → `[my]]db]]`. Load-bearing — must have a unit test for `[my]weird]db]`.

### CLI surface

7 flags, flags-only (no subcommands): `--access-mode`, `--connection-string`, `--query-timeout`, `--log-level`, `--validate`, `--version`/`-V`, `--help`/`-h`.

### Configuration

Every runtime tunable is env-overridable. Precedence: CLI flag > env var > default (exception: `MSSQL_CONNECTION_STRING` wins over `--connection-string`). Complete env var surface: `MSSQL_CONNECTION_STRING`, `MSSQL_ACCESS_MODE`, `MSSQL_QUERY_TIMEOUT`, `MSSQL_LOG_LEVEL`, `MSSQL_LOG_FILE`, `MSSQL_MAX_RESULT_BYTES`, `MSSQL_RETRY_COUNT`, `MSSQL_RETRY_INTERVAL`. Invalid values fail fast at startup. Unknown env vars ignored.

### Distribution

- **Primary**: NuGet `dotnet tool` package (mirrors DAB, sidesteps SNI redistribution). Cross-platform, no SNI license concern.
- **Secondary**: npm package wrapping self-contained .NET binary. Node shim in `npm/bin/` for immediate `npx` support. `install.js` postinstall downloads flat tarball from GitHub Releases per RID, verifies SHA256, overwrites shim with real binary. Fail-loudly on any error (no silent fallback). Linux/macOS self-contained (MIT-clean, managed SNI). Windows framework-dependent (SNI license blocks self-contained Windows).
- RIDs: `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` (self-contained), `win-x64` (framework-dependent).

### Logging

stderr always (stdout is MCP JSON-RPC). Optional file via `MSSQL_LOG_FILE`. Plain text, bracketed prefix convention. `--log-level` flag (default info) / `MSSQL_LOG_LEVEL` env var. Password obfuscation: `Password=...;` → `Password=***;` via custom logger filter on both sinks.

### Versioning

SemVer 2.0. First release: `0.1.0`. Tool surface stable from `0.1.0` (no breaking schema changes within `0.x`). Everything else (CLI, env vars, error shapes, return formats) unstable in `0.x`. Graduation to `1.0.0` requires all 5 triggers: 30 days production usage, Guard audit survived public review, tool surface stable for one minor cycle, distribution verified on all 5 RIDs, test coverage ≥ 80 unit tests with ≥ 30 Guard AST cases.

## Testing Decisions

### Testing philosophy

Test external behavior, not implementation details. A good test invokes the system the way a real caller would and asserts on the observable output. It does not break when internal refactoring changes class names, method signatures, or call patterns — only when behavior changes.

### Primary seam: MCP tool call boundary

The single highest seam. Every test invokes an MCP tool with inputs and asserts the `CallToolResult` output (the `TextContent` text and `isError` flag). This covers all 9 tools, both access modes, the Guard, error handling, type coercion, the byte cap, and transaction rollback — through one interface.

**Unit tests** (run in CI, no DB needed): inject a fake `ISqlConnection` that returns canned `SqlDataReader` results. The fake returns predefined rowsets, throws predefined `SqlException`s, and simulates timeouts. This tests the full tool → Guard → SqlExecutor → return-shape pipeline without a database.

**Integration tests** (opt-in via `INTEGRATION=true` env var): connect to a real Azure SQL Edge container. Tests the full pipeline including real ScriptDom parsing against real T-SQL, real connection pooling, real transaction rollback, real DMV queries.

Prior art: the MCP C# SDK's own test suite uses a similar tool-call-boundary pattern.

### Secondary seam: Guard AST attack vectors

A focused subset of unit tests that call the Guard directly (not through the tool seam) for the specific attack vectors from ADR-0006. These are safety-critical and deserve direct coverage:

- `SELECT 1` (accept)
- `SELECT 1; DROP TABLE x` (reject: multi-statement)
- `SELECT 1 GO DROP TABLE x` (reject: GO-separated)
- `BEGIN DROP TABLE x END` (reject: nested)
- `IF (1=1) DROP TABLE x` (reject: nested in IF)
- `WITH cte AS (SELECT 1) SELECT * FROM cte` (accept)
- `WITH cte AS (SELECT 1) DELETE FROM cte` (reject: CTE with DELETE)
- `SELECT * INTO #temp FROM Users` (reject: SELECT INTO)
- `SELECT * FROM OPENROWSET(...)` (reject: OPENROWSET)
- `-- DROP TABLE x` (accept: comment is not a statement)
- `` (empty string — reject: empty batch)
- Malformed SQL (reject: parse error with line/column)

Prior art: postgres-mcp's restricted mode tests use a similar AST-attack-vector pattern with pglast.

### Modules tested

- **`mssql-mcp.Core`**: Guard AST validation (primary unit test target), type coercion, `QuoteIdentifier`, password obfuscation, sentinel prefixing, CLI parsing.
- **`mssql-mcp.Tools`**: tool attribute wiring (all 9 tools have correct `ReadOnly`/`Destructive` annotations), tool dispatch (correct tool called for correct name), input schema validation.
- **`mssql-mcp` (App)**: startup config validation (invalid env vars fail fast), `--validate` flag behavior, stdio connection (integration only).

### Test framework

xUnit (matches the MCP SDK and Microsoft conventions). Integration tests tagged `[Trait("Category", "Integration")]`, filtered out of CI by default.

## Out of Scope

- **HTTP transports (SSE, streamable-http)**: v2 additive. stdio covers every MCP host in v1.
- **AD Password and AD Service Principal authentication**: v1.1. AD Default covers most cases.
- **Config file support**: v1 uses env vars + CLI flags only. Config files (JSON/TOML) deferred.
- **Result pagination via cursor tokens**: v1 uses `limit` params and the byte cap. Cursor-based pagination is a v2 consideration.
- **Stored procedure execution tool**: Restricted mode blocks `EXECUTE`; Unrestricted mode can run SPs via `execute_sql`. A dedicated SP tool with typed parameters from `sys.parameters` is a v2 consideration.
- **Write transaction support in Restricted mode**: v1 Restricted mode is strictly read-only with rollback. A "Restricted-write" mode with explicit transaction control is v2.
- **Log rotation**: v1 uses a single file path. Logrotate is the user's responsibility.
- **Structured JSON logging**: v1 is plain text. JSON structured logging is v2 additive.
- **GUI/dashboard**: No management UI. The server is a headless stdio process.
- **Multi-tenancy**: Single connection string, single database context (with optional cross-DB queries). No per-tenant isolation.
- **Query result caching**: No cache layer. Every query hits SQL Server.

## Further Notes

### ADRs

All 16 ADRs are in `docs/adr/`. Any implementation work should read the relevant ADRs first. Non-trivial changes require new ADRs per the contributing guide.

### Oracle's 3 implementation watch-out-fors

1. **`QuoteIdentifier` correctness is load-bearing** — a unit test for `[my]weird]db]` is mandatory. If this is wrong, cross-DB queries enable injection.
2. **Verify `Connection Reset=true` actually clears `SHOWPLAN_XML` state** — if not, queries after `explain_query` silently return plans instead of results. Test this in integration tests.
3. **Agent context window is the real constraint, not the 10MB byte cap** — only `list_objects` has a default `limit` in v1. If other tools return unbounded rows in practice, add default limits.

### Legal

- MIT licensed (copyright codegiveness).
- `THIRD-PARTY-NOTICES.md` lists 5 dependencies with their licenses and the SNI redistribution rationale.
- `NOTICE` file contains the trademark disclaimer and SQL Server CAL/multiplexing note.
- `SECURITY.md` defines the vulnerability disclosure process (GitHub private reporting, 48h/14d/90d SLA).

### References

- **postgres-mcp** (crystaldba) — functional gold standard. 8 tools, dual-mode, Python. We mirror its shape for MSSQL.
- **c0h1b4/mssql-mcp-server** — the "not yet perfect" MSSQL MCP. Single `query` tool, no safety rails, dead code. We fix every gap.
- **Microsoft DAB MCP** — official stance. NL2DAB model (anti-NL2SQL). Structured tools over raw SQL. We adopt the lesson: structured tools with real input schemas.
- **sqz** (ojuschugh1) — npm-wraps-native-binary reference. We copy the shim + postinstall download pattern.
