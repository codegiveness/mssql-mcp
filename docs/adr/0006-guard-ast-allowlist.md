# Guard AST allowlist: Visitor-based statement-type allowlist (SELECT + WITH) with targeted intra-statement blocklist

Restricted mode's Guard uses a two-layer AST validation strategy using Microsoft.SqlServer.TransactSql.ScriptDom (Microsoft's own T-SQL parser, MIT-licensed).

**Parsing entry point**: `TSql160Parser.Parse(TextReader, out IList<ParseError>)` — returns `TSqlScript` (NOT `ParseStatementList()`, which returns a flat `StatementList` and does not model `GO` batch separators). `Parse()` is the only entry point that correctly models the `TSqlScript → TSqlBatch → TSqlStatement` hierarchy, where `GO` creates new batches and `;` separates statements *within* a batch.

**Layer 1 — Statement-type allowlist via Visitor**:
Parse the SQL, then walk the AST with a `TSqlFragmentVisitor`. The Visitor overrides `Visit(SelectStatement)` to whitelist it. A catch-all override on the abstract `TSqlStatement` base records every concrete statement type encountered *anywhere* in the AST — including nested inside `BeginEndBlockStatement`, `IfStatement`, `WhileStatement`, and any other compound construct. After the walk, if the recorded set contains anything other than `SelectStatement`, reject.

This covers three attack vectors that naive indexing would miss:
1. **Multi-statement in one batch**: `SELECT 1; DROP TABLE x` → 1 batch, 2 statements. `Statements[0]` check would miss the DROP; the Visitor sees both.
2. **GO-separated batches**: `SELECT 1 GO DROP TABLE x` → 2 batches, 1 statement each. Iterating only `Batches[0]` would miss the DROP; the Visitor walks all batches.
3. **Nested statements**: `BEGIN DROP TABLE x END` and `IF (1=1) DROP TABLE x` → the outer `BeginEndBlockStatement`/`IfStatement` looks innocent, but the Visitor recurses into the nested `StatementList` and sees the inner `DropTableStatement`.

The Visitor's automatic recursion into compound constructs is why it's chosen over manual `batch.Statements` iteration — manual iteration would require explicit recursion into `BeginEndBlockStatement.StatementList.Statements`, `IfStatement.ThenStatement`, `IfStatement.ElseStatement`, `WhileStatement.Statement`, etc. The Visitor handles all of this via `script.Accept(visitor)`.

`SelectStatement` is the unifying type for both bare `SELECT` and `WITH ... SELECT` (CTE attached via `node.WithCtesAndXmlNamespaces`). `WITH ... DELETE/INSERT/UPDATE/MERGE` produces `DeleteStatement`/`InsertStatement`/`UpdateStatement`/`MergeStatement` respectively — correctly rejected.

**Layer 1 edge cases**:
- `TSqlStatementSnippet` (raw text fragment produced on partial parse failure): reject explicitly. It is not `SelectStatement`.
- Empty batches (0 statements = comment-only or empty input): reject with `[guard] No executable statement found.`
- Parse errors (`IList<ParseError>` non-empty): reject with `[guard] Parse error: {message} (line {line}, column {column})`. Do not proceed to Layer 2.

**Layer 2 — Targeted intra-statement blocklist via Visitor overrides**:
Even within an allowed `SelectStatement`, reject a small, targeted set of dangerous constructs by overriding the corresponding `Visit` methods:

| Construct | Visitor override | Why blocked |
|---|---|---|
| `SELECT ... INTO` | `Visit(SelectStatement)` → check `node.Into` | DDL disguised as SELECT — creates a table |
| `OPENROWSET('provider', ...)` in FROM | `Visit(OpenRowsetTableReference)` | Reads arbitrary files / remote servers (provider form) |
| `OPENROWSET(BULK 'file', ...)` in FROM | `Visit(BulkOpenRowset)` | Reads arbitrary local files (BULK form). **Note:** `BulkOpenRowset` is a separate AST node from `OpenRowsetTableReference` — they are siblings, not parent/child. Both must be blocked. |
| `OPENROWSET` for Cosmos DB | `Visit(OpenRowsetCosmos)` | Cross-service data access (Azure Synapse) |
| Internal OPENROWSET | `Visit(InternalOpenRowset)` | Internal variant — no legitimate agent use |
| `OPENQUERY` in FROM | `Visit(OpenQueryTableReference)` | Executes pass-through query on linked server |
| `OPENXML` in FROM | `Visit(OpenXmlTableReference)` | Parses XML document |
| `OPENDATASOURCE` in FROM | `Visit(AdHocTableReference)` | Ad-hoc access to remote data source |
| `EXECUTE AS` | `Visit(ExecuteAsStatement)` | Changes execution context — privilege escalation |
| Four-part (linked-server) names | `Visit(SchemaObjectName)` → check for 4 parts | Cross-server queries bypass server-level controls |
| `BulkInsertStatement` | `Visit(BulkInsertStatement)` | Belt-and-suspenders — bulk import is never read-only |

On rejection, the agent receives an MCP error result naming the rejecting layer and the reason: `[guard] Restricted mode: only SELECT and WITH statements are allowed. Got: {StatementType}` or `[guard] Restricted mode: SELECT ... INTO is not permitted.`

**Considered Options**:
- **Strategy 1 — allowlist statement types, denylist node types** (postgres-mcp pattern). Rejected: a node-type denylist is the trap postgres-mcp fell into — you can never enumerate every dangerous node type, and new SQL Server versions add new ones. The list drifts stale.
- **Strategy 3 — full node-type allowlist via ScriptDom Visitor**. Most secure but highest maintenance cost for v1. ScriptDom has hundreds of node types; whitelisting every one is a maintenance liability. Deferred to v2 if Strategy 2 proves too restrictive.
- **No intra-statement blocklist (trust statement-type + read-only transaction)**. Rejected: `SELECT ... INTO` is DDL disguised as SELECT, and `OPENROWSET(BULK ...)` reads arbitrary files. The read-only transaction backstop doesn't catch every intra-SELECT danger.
- **Manual `batch.Statements` iteration instead of Visitor**. Rejected: misses nested statements inside `BEGIN...END`, `IF`, `WHILE` without explicit recursion into every compound statement type. The Visitor handles recursion automatically.

**Consequences**:
- `DECLARE @t TABLE` patterns are not permitted in Restricted mode. Agents must refactor to a single CTE. This is a real ergonomics loss for complex analytical queries, accepted in exchange for a simpler, safer surface.
- `EXECUTE` of read-only system SPs (`sp_help`, `sp_columns`, etc.) is not permitted in Restricted mode. The structured discovery tools (`list_databases`, `list_schemas`, `list_objects`, `get_object_details`) replace them.
- The blocklist is small and targeted — it covers real T-SQL dangers without making the Guard unmaintainable. If a new dangerous construct emerges in a future SQL Server version, adding it to the blocklist is a one-line `Visit` override.
- The Guard sits *between* parsing and execution. If parsing fails (malformed T-SQL), the error surfaces to the agent as a parse error, distinct from a Guard rejection.
- `Unrestricted` mode skips Layer 1 and Layer 2 entirely — `execute_sql` runs whatever the agent sends. Safety there is the human's responsibility, signaled by `destructiveHint=True`.
