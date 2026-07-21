using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace mssql_mcp.Core;

/// <summary>
/// Classifies the statements in a T-SQL batch by parsing the SQL with ScriptDom and reading
/// the concrete statement types. Used by <c>execute_sql</c> in Unrestricted mode to build the
/// ADR-0009 status objects (DML returns <c>rows_affected</c>; DDL returns the affected object).
/// This is read-only parsing — it does NOT enforce the Restricted-mode allowlist (the Guard does
/// that). It reuses the same ScriptDom parser but a different visitor.
/// </summary>
public static class StatementClassifier
{
    /// <summary>
    /// Parses <paramref name="sql"/> and returns one <see cref="StatementInfo"/> per statement in
    /// the batch, in source order. Returns an empty list when the input parses to zero statements
    /// (empty batch, comment-only input) or when parsing fails — the caller is responsible for
    /// rejecting empty input before calling this if empty input should be an error.
    /// </summary>
    public static IReadOnlyList<StatementInfo> Classify(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        TSql160Parser parser = new(initialQuotedIdentifiers: false);
        IList<ParseError> errors = new List<ParseError>();
        TSqlFragment? fragment;
        try
        {
            using StringReader reader = new(sql);
            fragment = parser.Parse(reader, out errors);
        }
        catch (Exception)
        {
            // Parse failure is not an exception for classification — the caller already decided
            // to execute the SQL (Unrestricted mode bypasses the Guard). Returning an empty list
            // lets the caller proceed with the raw-SQL path. The Guard's strict path is the
            // source of parse-error rejections; classification is best-effort type detection.
            return Array.Empty<StatementInfo>();
        }

        if (errors.Count > 0 || fragment is not TSqlScript script)
        {
            return Array.Empty<StatementInfo>();
        }

        List<StatementInfo> result = new();
        foreach (TSqlBatch batch in script.Batches)
        {
            foreach (TSqlStatement stmt in batch.Statements)
            {
                StatementInfo info = ClassifyStatement(stmt);
                result.Add(info);
            }
        }
        return result;
    }

    private static StatementInfo ClassifyStatement(TSqlStatement stmt) => new(
        StatementType: MapStatementType(stmt),
        ObjectName: MapObjectName(stmt),
        RowsAffected: -1);

    private static string MapStatementType(TSqlStatement stmt) => stmt switch
    {
        InsertStatement => "INSERT",
        UpdateStatement => "UPDATE",
        DeleteStatement => "DELETE",
        MergeStatement => "MERGE",
        CreateTableStatement => "CREATE_TABLE",
        AlterTableStatement => "ALTER_TABLE",
        DropTableStatement => "DROP_TABLE",
        CreateIndexStatement => "CREATE_INDEX",
        DropIndexStatement => "DROP_INDEX",
        CreateProcedureStatement => "CREATE_PROCEDURE",
        AlterProcedureStatement => "ALTER_PROCEDURE",
        DropProcedureStatement => "DROP_PROCEDURE",
        CreateViewStatement => "CREATE_VIEW",
        AlterViewStatement => "ALTER_VIEW",
        DropViewStatement => "DROP_VIEW",
        CreateFunctionStatement => "CREATE_FUNCTION",
        AlterFunctionStatement => "ALTER_FUNCTION",
        DropFunctionStatement => "DROP_FUNCTION",
        TruncateTableStatement => "TRUNCATE_TABLE",
        BulkInsertStatement => "BULK_INSERT",
        SelectStatement => "SELECT",
        _ => "UNKNOWN",
    };

    private static string? MapObjectName(TSqlStatement stmt)
    {
        // Try the most common DDL shapes first. For statements without a clear single object
        // name (DROP INDEX, etc.), return null — the status object simply omits the "object" field.
        if (stmt is CreateTableStatement createTable && createTable.SchemaObjectName is not null)
        {
            return FormatSchemaObjectName(createTable.SchemaObjectName);
        }
        if (stmt is AlterTableStatement alterTable && alterTable.SchemaObjectName is not null)
        {
            return FormatSchemaObjectName(alterTable.SchemaObjectName);
        }
        if (stmt is DropTableStatement dropTable && dropTable.Objects.Count > 0)
        {
            return FormatSchemaObjectName(dropTable.Objects[0]);
        }
        if (stmt is CreateIndexStatement createIndex && createIndex.OnName is not null)
        {
            return FormatSchemaObjectName(createIndex.OnName);
        }
        if (stmt is DropIndexStatement dropIndex && dropIndex.DropIndexClauses.Count > 0
            && dropIndex.DropIndexClauses[0] is DropIndexClause clause && clause.Object is not null)
        {
            return FormatSchemaObjectName(clause.Object);
        }
        if (stmt is CreateProcedureStatement createProc && createProc.ProcedureReference is not null
            && createProc.ProcedureReference.Name is not null)
        {
            return FormatSchemaObjectName(createProc.ProcedureReference.Name);
        }
        if (stmt is AlterProcedureStatement alterProc && alterProc.ProcedureReference is not null
            && alterProc.ProcedureReference.Name is not null)
        {
            return FormatSchemaObjectName(alterProc.ProcedureReference.Name);
        }
        if (stmt is DropProcedureStatement dropProc && dropProc.Objects.Count > 0)
        {
            return FormatSchemaObjectName(dropProc.Objects[0]);
        }
        if (stmt is CreateViewStatement createView && createView.SchemaObjectName is not null)
        {
            return FormatSchemaObjectName(createView.SchemaObjectName);
        }
        if (stmt is AlterViewStatement alterView && alterView.SchemaObjectName is not null)
        {
            return FormatSchemaObjectName(alterView.SchemaObjectName);
        }
        if (stmt is DropViewStatement dropView && dropView.Objects.Count > 0)
        {
            return FormatSchemaObjectName(dropView.Objects[0]);
        }
        if (stmt is CreateFunctionStatement createFunc && createFunc.Name is not null)
        {
            return FormatSchemaObjectName(createFunc.Name);
        }
        if (stmt is AlterFunctionStatement alterFunc && alterFunc.Name is not null)
        {
            return FormatSchemaObjectName(alterFunc.Name);
        }
        if (stmt is DropFunctionStatement dropFunc && dropFunc.Objects.Count > 0)
        {
            return FormatSchemaObjectName(dropFunc.Objects[0]);
        }
        if (stmt is TruncateTableStatement truncate && truncate.TableName is not null)
        {
            return FormatSchemaObjectName(truncate.TableName);
        }
        return null;
    }

    /// <summary>
    /// Formats a <see cref="SchemaObjectName"/> as a dotted string. Includes database/schema
    /// parts only when present (most agents see <c>dbo.TableName</c>; cross-DB writes show
    /// <c>OtherDb.dbo.TableName</c>). Brackets in the source are preserved.
    /// </summary>
    private static string FormatSchemaObjectName(SchemaObjectName name)
    {
        // Identifiers already carry their own brackets (or unquoted form) from the parser.
        // Join with dots — matches the ADR-0009 example shape "dbo.NewTable".
        return string.Join(".", name.Identifiers.Select(i => i.Value));
    }
}

/// <summary>
/// One T-SQL statement extracted from a batch, with its type and (for DDL) the affected object.
/// </summary>
/// <param name="StatementType">Canonical type name like <c>UPDATE</c>, <c>CREATE_TABLE</c>. <c>SELECT</c> for SELECT. <c>UNKNOWN</c> for anything not in the explicit map.</param>
/// <param name="ObjectName">For DDL: the affected object (e.g. <c>dbo.NewTable</c>). Null for DML and statements without a single clear object name.</param>
/// <param name="RowsAffected">Always <c>-1</c> here — populated by <c>execute_sql</c> after execution via <c>ExecuteNonQueryAsync</c>. <c>-1</c> is the SQL Server sentinel for "rows-affected not reported".</param>
public sealed record StatementInfo(string StatementType, string? ObjectName, int RowsAffected);
