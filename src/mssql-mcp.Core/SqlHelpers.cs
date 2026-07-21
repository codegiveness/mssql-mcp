namespace mssql_mcp.Core;

/// <summary>
/// Cross-database safety helpers per ADR-0016. <see cref="QuoteIdentifier"/> is load-bearing
/// for SQL injection prevention — database names are injected into <c>[{db}].sys.*</c> FROM
/// clauses (which cannot be parameterized), so they MUST be bracketed with internal <c>]</c>
/// doubled. <see cref="ValidateDatabaseAsync"/> runs the three-check cross-DB safety rule
/// (exists / online / multi-user) before any cross-DB query.
/// </summary>
public static class SqlHelpers
{
    private const string ValidateDatabaseSql =
        """
        SELECT state_desc, user_access_desc
        FROM sys.databases
        WHERE name = @database
        """;

    /// <summary>
    /// Wraps a database name in a bracketed identifier, doubling internal <c>]</c> to <c>]]</c>.
    /// Required before injecting a user-supplied database name into a SQL FROM clause as
    /// <c>[{db}].sys.*</c> — SQL Server doesn't support parameterized identifiers, so the
    /// brackets are the only barrier against SQL injection.
    /// </summary>
    public static string QuoteIdentifier(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return "[" + name.Replace("]", "]]") + "]";
    }

    /// <summary>
    /// Validates the three cross-DB safety checks against <c>sys.databases</c>:
    /// (1) database exists, (2) <c>state_desc = 'ONLINE'</c>, (3) <c>user_access_desc = 'MULTI_USER'</c>.
    /// The database NAME is passed as a parameter (safe). The caller is still responsible for
    /// using <see cref="QuoteIdentifier"/> when building the actual <c>[{db}].sys.*</c> query.
    /// </summary>
    public static async Task<DatabaseValidationResult> ValidateDatabaseAsync(
        ISqlExecutor executor,
        string database,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);

        Dictionary<string, object> parameters = new() { ["database"] = database };

        List<Dictionary<string, object?>> rows;
        try
        {
            rows = await executor.ExecuteQueryAsync(ValidateDatabaseSql, parameters, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }

        if (rows.Count == 0)
        {
            return new DatabaseValidationResult(Valid: false, Error: $"Database '{database}' does not exist.");
        }

        Dictionary<string, object?> row = rows[0];
        string stateDesc = row.TryGetValue("state_desc", out object? s) && s is string sv ? sv : string.Empty;
        if (stateDesc != "ONLINE")
        {
            return new DatabaseValidationResult(Valid: false,
                Error: $"Database '{database}' is not online (state: {stateDesc}).");
        }

        string userAccessDesc = row.TryGetValue("user_access_desc", out object? u) && u is string uv ? uv : string.Empty;
        if (userAccessDesc != "MULTI_USER")
        {
            return new DatabaseValidationResult(Valid: false,
                Error: $"Database '{database}' is not multi-user (access: {userAccessDesc}).");
        }

        return new DatabaseValidationResult(Valid: true, Error: null);
    }
}

/// <summary>
/// Result of <see cref="SqlHelpers.ValidateDatabaseAsync"/>. When <see cref="Valid"/> is false,
/// <see cref="Error"/> carries a specific message naming which check failed.
/// </summary>
public sealed record DatabaseValidationResult(bool Valid, string? Error);
