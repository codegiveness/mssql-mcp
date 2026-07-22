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
    public const string FailurePrefix = "[startup] Connection validation failed ";

    /// <summary>
    /// Opens a connection, runs <c>SELECT 1</c>, closes it.
    /// Returns <c>(true, SuccessMessage)</c> on success or
    /// <c>(false, FailurePrefix + "[tag]: " + obfuscated error)</c> on failure. Never throws.
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
            return (false, FailurePrefix + "[" + ClassifyFailure(ex) + "]: " + PasswordObfuscator.Obfuscate(ex.Message));
        }
    }

    /// <summary>
    /// Classifies <paramref name="ex"/> into a failure-layer tag for the validation message.
    /// SqlException numbers per Microsoft documentation (ticket 31):
    /// <list type="bullet">
    /// <item>-2: timeout expired → <c>timeout</c></item>
    /// <item>53, 64, 233, 10060: connection-class network errors → <c>connection</c></item>
    /// <item>18456: login failed → <c>auth</c></item>
    /// <item>-2076: TLS certificate validation failure → <c>certificate</c></item>
    /// </list>
    /// An unmatched <see cref="SqlException"/> defaults to <c>connection</c> (the validator's
    /// job is reachability — an unknown SQL error during pre-flight is most likely a
    /// connection-layer failure). <see cref="OperationCanceledException"/> (incl. <see cref="TaskCanceledException"/>)
    /// and <see cref="TimeoutException"/> map to <c>timeout</c>. Any other exception type is
    /// tagged by its type name, lowercased, with the <c>Exception</c> suffix stripped
    /// (e.g. <c>ArgumentException</c> → <c>argument</c>).
    /// </summary>
    internal static string ClassifyFailure(Exception ex)
    {
        if (ex is OperationCanceledException or TimeoutException)
        {
            return "timeout";
        }

        if (ex is SqlException sqlEx)
        {
            int number = sqlEx.Number;
            return number switch
            {
                -2 => "timeout",
                53 or 64 or 233 or 10060 => "connection",
                18456 => "auth",
                -2076 => "certificate",
                _ => "connection",
            };
        }

        string typeName = ex.GetType().Name;
        const string suffix = "Exception";
        if (typeName.EndsWith(suffix, StringComparison.Ordinal))
        {
            typeName = typeName[..^suffix.Length];
        }
        return typeName.ToLowerInvariant();
    }
}
