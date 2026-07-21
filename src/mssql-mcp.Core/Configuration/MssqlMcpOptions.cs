using System.Globalization;

namespace mssql_mcp.Core.Configuration;

/// <summary>
/// Resolved runtime configuration for the mssql-mcp server.
/// Precedence: CLI flag &gt; env var &gt; hardcoded default, except
/// MSSQL_CONNECTION_STRING wins over --connection-string (secrets live in env, not argv).
/// </summary>
public sealed class MssqlMcpOptions
{
    public const string EnvConnectionString = "MSSQL_CONNECTION_STRING";
    public const string EnvAccessMode = "MSSQL_ACCESS_MODE";
    public const string EnvQueryTimeout = "MSSQL_QUERY_TIMEOUT";
    public const string EnvLogLevel = "MSSQL_LOG_LEVEL";
    public const string EnvLogFile = "MSSQL_LOG_FILE";
    public const string EnvMaxResultBytes = "MSSQL_MAX_RESULT_BYTES";
    public const string EnvRetryCount = "MSSQL_RETRY_COUNT";
    public const string EnvRetryIntervalMin = "MSSQL_RETRY_INTERVAL";

    public const string CliConnectionString = "--connection-string";
    public const string CliAccessMode = "--access-mode";
    public const string CliQueryTimeout = "--query-timeout";
    public const string CliLogLevel = "--log-level";

    public string ConnectionString { get; set; } = string.Empty;
    public AccessMode AccessMode { get; set; } = AccessMode.Restricted;
    public int QueryTimeout { get; set; } = 30;
    public string LogLevel { get; set; } = "info";
    public string? LogFile { get; set; }
    public long MaxResultBytes { get; set; } = 10 * 1024 * 1024; // 10 MB per ADR-0003
    public int RetryCount { get; set; } = 3;
    public int RetryIntervalMin { get; set; } = 2;
    public int RetryIntervalMax { get; set; } = 10;

    /// <summary>
    /// Parses args + env into a validated <see cref="MssqlMcpOptions"/>.
    /// Throws <see cref="InvalidOperationException"/> with a clear [startup] message on any invalid value.
    /// </summary>
    public static MssqlMcpOptions Parse(string[] args, System.Collections.IDictionary env)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);

        var options = new MssqlMcpOptions();

        // Connection string: env wins over CLI (secrets live in env, not argv) per ADR-0015.
        string? cliConnStr = GetCliValue(args, CliConnectionString);
        string? envConnStr = GetEnv(env, EnvConnectionString);
        string? resolvedConnStr = envConnStr ?? cliConnStr;
        if (string.IsNullOrWhiteSpace(resolvedConnStr))
        {
            throw new InvalidOperationException(
                "[startup] Missing SQL Server connection string. Set MSSQL_CONNECTION_STRING env var or pass --connection-string.");
        }
        options.ConnectionString = resolvedConnStr;

        // Access mode: CLI > env > default (Restricted).
        string? accessModeRaw = GetCliValue(args, CliAccessMode) ?? GetEnv(env, EnvAccessMode);
        if (!string.IsNullOrWhiteSpace(accessModeRaw))
        {
            if (!Enum.TryParse<AccessMode>(accessModeRaw, ignoreCase: true, out AccessMode parsedMode))
            {
                throw new InvalidOperationException(
                    $"[startup] Invalid access mode '{accessModeRaw}'. Accepted values: restricted, unrestricted.");
            }
            options.AccessMode = parsedMode;
        }

        // Query timeout: CLI > env > default (30 restricted / 0 unrestricted).
        int defaultQueryTimeout = options.AccessMode == AccessMode.Unrestricted ? 0 : 30;
        string? queryTimeoutRaw = GetCliValue(args, CliQueryTimeout) ?? GetEnv(env, EnvQueryTimeout);
        if (string.IsNullOrWhiteSpace(queryTimeoutRaw))
        {
            options.QueryTimeout = defaultQueryTimeout;
        }
        else
        {
            if (!int.TryParse(queryTimeoutRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedTimeout) || parsedTimeout < 0)
            {
                throw new InvalidOperationException(
                    $"[startup] Invalid query timeout '{queryTimeoutRaw}'. Must be a non-negative integer.");
            }
            options.QueryTimeout = parsedTimeout;
        }

        // Log level: CLI > env > default (info).
        string? logLevelRaw = GetCliValue(args, CliLogLevel) ?? GetEnv(env, EnvLogLevel);
        if (!string.IsNullOrWhiteSpace(logLevelRaw))
        {
            options.LogLevel = logLevelRaw;
        }

        // Log file: env only (no CLI flag per ADR-0015).
        string? logFileRaw = GetEnv(env, EnvLogFile);
        if (!string.IsNullOrWhiteSpace(logFileRaw))
        {
            options.LogFile = logFileRaw;
        }

        // Max result bytes: env only.
        string? maxResultRaw = GetEnv(env, EnvMaxResultBytes);
        if (!string.IsNullOrWhiteSpace(maxResultRaw))
        {
            if (!long.TryParse(maxResultRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedMax) || parsedMax < 0)
            {
                throw new InvalidOperationException(
                    $"[startup] Invalid max result bytes '{maxResultRaw}'. Must be a non-negative integer.");
            }
            options.MaxResultBytes = parsedMax;
        }

        // Retry count: env only.
        string? retryCountRaw = GetEnv(env, EnvRetryCount);
        if (!string.IsNullOrWhiteSpace(retryCountRaw))
        {
            if (!int.TryParse(retryCountRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRetry) || parsedRetry < 0)
            {
                throw new InvalidOperationException(
                    $"[startup] Invalid retry count '{retryCountRaw}'. Must be a non-negative integer.");
            }
            options.RetryCount = parsedRetry;
        }

        // Retry interval min/max: env only. MSSQL_RETRY_INTERVAL sets the min backoff.
        // The max stays at the default (10s) — ADR-0015 only defines MSSQL_RETRY_INTERVAL for min.
        string? retryMinRaw = GetEnv(env, EnvRetryIntervalMin);
        if (!string.IsNullOrWhiteSpace(retryMinRaw))
        {
            if (!int.TryParse(retryMinRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMin) || parsedMin < 0)
            {
                throw new InvalidOperationException(
                    $"[startup] Invalid retry interval min '{retryMinRaw}'. Must be a non-negative integer.");
            }
            options.RetryIntervalMin = parsedMin;
        }

        return options;
    }

    /// <summary>
    /// Extracts a --flag value from argv. Supports both "--flag value" and "--flag=value" forms.
    /// Returns null if the flag is not present.
    /// </summary>
    private static string? GetCliValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            string eqForm = flag + "=";
            if (token.StartsWith(eqForm, StringComparison.Ordinal))
            {
                return token[eqForm.Length..];
            }
            if (token == flag && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return null;
    }

    /// <summary>
    /// Case-insensitive env var lookup. Env dictionaries may use either string (Environment.GetEnvironmentVariables)
    /// or string? values.
    /// </summary>
    private static string? GetEnv(System.Collections.IDictionary env, string name)
    {
        // Environment.GetEnvironmentVariables() returns case-sensitive keys on Linux.
        foreach (System.Collections.DictionaryEntry entry in env)
        {
            if (entry.Key is string key && string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value as string;
            }
        }
        return null;
    }
}
