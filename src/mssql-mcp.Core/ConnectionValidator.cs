using Microsoft.Data.SqlClient;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Logging;

namespace mssql_mcp.Core;

/// <summary>
/// Pre-flight connection check for the <c>--validate</c> CLI flag (ticket 10).
/// Opens a connection, runs <c>SELECT 1</c>, closes — does not start the MCP server.
/// Retries transient failures via the same <see cref="SqlRetryLogicBaseProvider"/> shape as
/// <see cref="SqlExecutor"/> (ADR-0004); passwords in error messages are obfuscated.
/// </summary>
public static class ConnectionValidator
{
    public const string SuccessMessage = "[startup] Connection validated successfully.";
    public const string FailurePrefix = "[startup] Connection validation failed: ";

    /// <summary>
    /// Opens a connection, runs <c>SELECT 1</c>, closes it.
    /// Returns <c>(true, SuccessMessage)</c> on success or
    /// <c>(false, FailurePrefix + obfuscated error)</c> on failure. Never throws.
    /// </summary>
    public static async Task<(bool Success, string Message)> ValidateAsync(
        MssqlMcpOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        SqlRetryLogicBaseProvider retryProvider = SqlExecutor.BuildRetryProvider(
            options.RetryCount, options.RetryIntervalMin, options.RetryIntervalMax,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SqlExecutor>.Instance);

        try
        {
            using SqlConnection conn = new(options.ConnectionString) { RetryLogicProvider = retryProvider };
            await conn.OpenAsync(ct).ConfigureAwait(false);
            using SqlCommand cmd = new("SELECT 1", conn) { RetryLogicProvider = retryProvider };
            await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return (true, SuccessMessage);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (false, FailurePrefix + PasswordObfuscator.Obfuscate(ex.Message));
        }
    }
}
