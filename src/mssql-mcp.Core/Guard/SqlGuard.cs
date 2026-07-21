using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using mssql_mcp.Core.Configuration;

namespace mssql_mcp.Core.Guard;

/// <summary>
/// Restricted-mode Guard. Parses SQL with ScriptDom's <see cref="TSql160Parser"/> and walks the AST
/// with a <see cref="TSqlFragmentVisitor"/> to enforce the two-layer allowlist/blocklist per ADR-0006.
/// On accept, returns the SQL wrapped in the sentinel + transaction pair per ADR-0007.
/// On reject, returns a structured <see cref="GuardRejection"/> per ADR-0010.
/// In Unrestricted mode, validation is skipped entirely.
/// </summary>
public sealed class SqlGuard : IGuard
{
    private const string Sentinel = "/* mssql-mcp */";

    private readonly MssqlMcpOptions _options;
    private readonly ILogger<SqlGuard> _logger;

    public SqlGuard(MssqlMcpOptions options, ILogger<SqlGuard> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public GuardResult Validate(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        // Unrestricted mode skips both layers per ADR-0006.
        if (_options.AccessMode == AccessMode.Unrestricted)
        {
            // Even in Unrestricted mode, refuse empty input — nothing to execute.
            if (string.IsNullOrWhiteSpace(sql))
            {
                return GuardResult.Reject(new GuardRejection(
                    rule: "empty_batch",
                    detail: "[guard] No executable statement found."));
            }
            return GuardResult.Accept(sql);
        }

        return ValidateStrict(sql);
    }

    /// <inheritdoc/>
    public GuardResult ValidateStrict(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        // Empty/whitespace-only input is an empty batch (no statement), not a parse error.
        if (string.IsNullOrWhiteSpace(sql))
        {
            return GuardResult.Reject(new GuardRejection(
                rule: "empty_batch",
                detail: "[guard] No executable statement found."));
        }

        TSql160Parser parser = new(initialQuotedIdentifiers: false);
        IList<ParseError> errors = new List<ParseError>();
        TSqlFragment? fragment;
        try
        {
            using StringReader reader = new(sql);
            fragment = parser.Parse(reader, out errors);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "[guard] Parser threw unexpected exception");
            return GuardResult.Reject(new GuardRejection(
                rule: "parse_error",
                detail: $"Parser threw: {ex.Message}"));
        }

        // Parse errors → reject before walking the AST.
        if (errors.Count > 0)
        {
            ParseError first = errors[0];
            return GuardResult.Reject(new GuardRejection(
                rule: "parse_error",
                detail: first.Message,
                line: first.Line,
                column: first.Column));
        }

        if (fragment is not TSqlScript script)
        {
            // Parse returned no errors but no TSqlScript — treat as parse failure.
            return GuardResult.Reject(new GuardRejection(
                rule: "parse_error",
                detail: "Parser returned no script fragment."));
        }

        // Empty batch — no statement at all (comment-only or whitespace input).
        // A comment-only input produces 0 batches; an empty input also produces 0 batches.
        int statementCount = 0;
        foreach (TSqlBatch batch in script.Batches)
        {
            statementCount += batch.Statements.Count;
        }
        if (statementCount == 0)
        {
            return GuardResult.Reject(new GuardRejection(
                rule: "empty_batch",
                detail: "[guard] No executable statement found."));
        }

        // Walk the AST. The visitor collects the first rejection encountered.
        ValidationVisitor visitor = new();
        script.Accept(visitor);

        if (visitor.Rejection is not null)
        {
            return GuardResult.Reject(visitor.Rejection);
        }

        // Layer 1 also requires at least one SelectStatement to have been seen.
        // An empty visitor walk (0 statements) was already caught above, but a batch of
        // non-statement fragments would have triggered a Layer-1 rejection in Visit(TSqlStatement).
        if (!visitor.SawSelectStatement)
        {
            return GuardResult.Reject(new GuardRejection(
                rule: "non_select_statement",
                detail: "[guard] No SELECT/WITH statement found in input."));
        }

        string wrapped = $"{Sentinel}\nBEGIN TRANSACTION\n{sql}\nROLLBACK TRANSACTION";
        return GuardResult.Accept(wrapped);
    }

    /// <summary>
    /// TSqlFragmentVisitor that records the first rejection encountered while walking the AST.
    /// Layer 1: <see cref="Visit(TSqlStatement)"/> rejects any statement type other than SelectStatement
    /// anywhere in the AST (including nested inside BEGIN/END, IF, WHILE — the visitor recurses automatically).
    /// Layer 2: targeted overrides reject SELECT INTO, OPENROWSET/QUERY/XML/DATASOURCE, EXECUTE AS,
    /// four-part names, and BULK INSERT.
    /// </summary>
    private sealed class ValidationVisitor : TSqlFragmentVisitor
    {
        /// <summary>First rejection encountered, or null if none yet.</summary>
        public GuardRejection? Rejection { get; private set; }

        /// <summary>True once a SelectStatement has been visited (used to detect "no SELECT at all").</summary>
        public bool SawSelectStatement { get; private set; }

        // ---- Layer 1: statement-type allowlist ----

        public override void Visit(TSqlStatement node)
        {
            // Snippets are produced on partial-parse fallback — reject explicitly even though they
            // are technically a TSqlStatement subclass.
            if (node is TSqlStatementSnippet snippet)
            {
                SetRejection(new GuardRejection(
                    rule: "statement_snippet",
                    detail: snippet.Script ?? "[guard] Partial parse produced a statement snippet.",
                    statementType: nameof(TSqlStatementSnippet)));
                return;
            }

            // SelectStatement is the only allowed top-level statement type (covers bare SELECT and
            // WITH ... SELECT — CTEs attach via WithCtesAndXmlNamespaces on SelectStatement).
            // WITH ... DELETE/INSERT/UPDATE/MERGE produce DeleteStatement/etc., correctly rejected here.
            if (node is SelectStatement)
            {
                // Mark as seen but let the recursion continue so Layer 2 overrides can fire.
                // Visit(SelectStatement) below handles Into check.
                return;
            }

            // ExecuteAsStatement and BulkInsertStatement have Layer-2-specific rule names
            // (execute_as, bulk_insert). Skip the catch-all here so the targeted override fires
            // and produces the more specific rule. Other non-SELECT statement types fall through
            // to the generic non_select_statement rejection.
            if (node is ExecuteAsStatement || node is BulkInsertStatement)
            {
                return;
            }

            SetRejection(new GuardRejection(
                rule: "non_select_statement",
                detail: $"[guard] Restricted mode: only SELECT and WITH statements are allowed. Got: {node.GetType().Name}",
                statementType: node.GetType().Name));
        }

        public override void Visit(SelectStatement node)
        {
            SawSelectStatement = true;

            // Layer 2: SELECT ... INTO is DDL disguised as SELECT — reject.
            if (node.Into is not null)
            {
                SetRejection(new GuardRejection(
                    rule: "select_into",
                    detail: "[guard] Restricted mode: SELECT ... INTO is not permitted."));
            }
        }

        // ---- Layer 2: intra-SELECT blocklist ----

        public override void Visit(OpenRowsetTableReference node)
        {
            SetRejection(new GuardRejection(
                rule: "openrowset",
                detail: "[guard] Restricted mode: OPENROWSET is not permitted."));
        }

        public override void Visit(OpenQueryTableReference node)
        {
            SetRejection(new GuardRejection(
                rule: "openquery",
                detail: "[guard] Restricted mode: OPENQUERY is not permitted."));
        }

        public override void Visit(OpenXmlTableReference node)
        {
            SetRejection(new GuardRejection(
                rule: "openxml",
                detail: "[guard] Restricted mode: OPENXML is not permitted."));
        }

        public override void Visit(AdHocTableReference node)
        {
            // AdHocTableReference is the AST node produced by OPENDATASOURCE('PROVIDER', 'CONN').db.schema.table.
            SetRejection(new GuardRejection(
                rule: "opendatasource",
                detail: "[guard] Restricted mode: OPENDATASOURCE is not permitted."));
        }

        public override void Visit(BulkOpenRowset node)
        {
            // BulkOpenRowset is the AST node for OPENROWSET(BULK 'file', SINGLE_CLOB) — a file-read
            // vector that does NOT inherit from OpenRowsetTableReference. Caught separately per
            // Oracle review (Critical bypass fix).
            SetRejection(new GuardRejection(
                rule: "openrowset_bulk",
                detail: "[guard] Restricted mode: OPENROWSET(BULK ...) is not permitted."));
        }

        public override void Visit(InternalOpenRowset node)
        {
            // InternalOpenRowset — internal OPENROWSET variant used by system queries.
            // Blocked defensively — no legitimate agent use.
            SetRejection(new GuardRejection(
                rule: "openrowset_internal",
                detail: "[guard] Restricted mode: internal OPENROWSET is not permitted."));
        }

        public override void Visit(OpenRowsetCosmos node)
        {
            // OpenRowsetCosmos — OPENROWSET Cosmos DB access (Azure Synapse Analytics).
            // Blocked — cross-service data access.
            SetRejection(new GuardRejection(
                rule: "openrowset_cosmos",
                detail: "[guard] Restricted mode: OPENROWSET for Cosmos DB is not permitted."));
        }

        public override void Visit(ExecuteAsStatement node)
        {
            SetRejection(new GuardRejection(
                rule: "execute_as",
                detail: "[guard] Restricted mode: EXECUTE AS is not permitted."));
        }

        public override void Visit(BulkInsertStatement node)
        {
            SetRejection(new GuardRejection(
                rule: "bulk_insert",
                detail: "[guard] Restricted mode: BULK INSERT is not permitted."));
        }

        public override void Visit(SchemaObjectName node)
        {
            // Four-part names (linked-server references) — Server.Database.Schema.Object.
            // Identifiers.Count is the part count.
            if (node.Identifiers.Count >= 4)
            {
                SetRejection(new GuardRejection(
                    rule: "four_part_name",
                    detail: "[guard] Restricted mode: four-part (linked-server) names are not permitted."));
            }
        }

        private void SetRejection(GuardRejection rejection)
        {
            // First rejection wins — visitor order is Layer 1 (Visit(TSqlStatement)) before
            // Layer 2 (Visit(SelectStatement), etc.), matching ADR-0006 precedence.
            Rejection ??= rejection;
        }
    }
}
