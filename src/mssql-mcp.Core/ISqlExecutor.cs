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
}
