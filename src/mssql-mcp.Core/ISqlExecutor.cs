namespace mssql_mcp.Core;

/// <summary>
/// Abstraction over SQL execution, returning type-coerced rows per ADR-0009.
/// Tools depend on this interface (not on SqlConnection) so tests can fake it cleanly.
/// </summary>
public interface ISqlExecutor
{
    /// <summary>
    /// Executes a SQL query, retaining rows within a conservative JSON byte budget.
    /// Zero disables the budget; there is no row-count cap.
    /// </summary>
    Task<SqlQueryResult> ExecuteQueryAsync(string sql, long maxResultBytes, CancellationToken ct);

    /// <summary>
    /// Executes a parameterized SQL query with the same byte-budget semantics.
    /// </summary>
    /// <param name="sql">SQL text containing @-prefixed parameter placeholders.</param>
    /// <param name="parameters">Map from parameter name (without @) to value. Null = no parameters.</param>
    /// <param name="maxResultBytes">Positive JSON byte budget; zero disables early termination.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Used by discovery tools that need to safely pass user input (schema names, object names,
    /// database lookup names) into queries. SQL Server doesn't support parameterized identifiers
    /// in the FROM clause — for database names injected into <c>[{db}].sys.objects</c>, use
    /// <see cref="SqlHelpers.QuoteIdentifier"/> instead of parameters.
    /// </remarks>
    Task<SqlQueryResult> ExecuteQueryAsync(
        string sql,
        IReadOnlyDictionary<string, object>? parameters,
        long maxResultBytes,
        CancellationToken ct);

    /// <summary>
    /// Executes a non-query SQL batch (DML/DDL) and returns the cumulative rows-affected count.
    /// Used by <c>execute_sql</c> in Unrestricted mode per ADR-0009 (non-rowset return shape).
    /// </summary>
    /// <param name="sql">SQL text to execute. Caller is responsible for classification and (in Restricted mode) Guard validation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rows-affected count returned by <c>SqlCommand.ExecuteNonQueryAsync</c>. Returns <c>-1</c> for statements that don't affect rows (most DDL).</returns>
    Task<int> ExecuteNonQueryAsync(string sql, CancellationToken ct);

    /// <summary>
    /// Executes <c>SET SHOWPLAN_XML ON</c>, runs the supplied SQL, and returns the
    /// SHOWPLAN_XML string that SQL Server emits as a single-row, single-column result
    /// set (the query is not actually executed — SQL Server returns the estimated plan).
    /// Always runs <c>SET SHOWPLAN_XML OFF</c> in a finally block so the session-scoped
    /// setting cannot leak onto a pooled connection (ADR-0016 Oracle watch-out-for #2).
    /// </summary>
    /// <param name="sql">SQL text to plan. Must already be Guard-validated.</param>
    /// <param name="maxResultBytes">Positive raw XML UTF-8 limit; zero for unlimited raw XML or summary processing.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> ExecuteShowPlanXmlAsync(string sql, long maxResultBytes, CancellationToken ct);
}
