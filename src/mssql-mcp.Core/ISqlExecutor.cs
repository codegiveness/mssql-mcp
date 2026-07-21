namespace mssql_mcp.Core;

/// <summary>
/// Abstraction over SQL execution, returning type-coerced rows per ADR-0009.
/// Tools depend on this interface (not on SqlConnection) so tests can fake it cleanly.
/// </summary>
public interface ISqlExecutor
{
    /// <summary>
    /// Executes a SQL query and returns the result rows as a list of dictionaries
    /// keyed by column name with values coerced per ADR-0009.
    /// </summary>
    Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(string sql, CancellationToken ct);

    /// <summary>
    /// Executes a parameterized SQL query and returns the result rows as a list of
    /// dictionaries keyed by column name with values coerced per ADR-0009.
    /// </summary>
    /// <param name="sql">SQL text containing @-prefixed parameter placeholders.</param>
    /// <param name="parameters">Map from parameter name (without @) to value. Null = no parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Used by discovery tools that need to safely pass user input (schema names, object names,
    /// database lookup names) into queries. SQL Server doesn't support parameterized identifiers
    /// in the FROM clause — for database names injected into <c>[{db}].sys.objects</c>, use
    /// <see cref="SqlHelpers.QuoteIdentifier"/> instead of parameters.
    /// </remarks>
    Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(
        string sql,
        IReadOnlyDictionary<string, object>? parameters,
        CancellationToken ct);
}
