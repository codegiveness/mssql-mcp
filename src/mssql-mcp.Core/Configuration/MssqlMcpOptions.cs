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
    public const string EnvRetryIntervalMax = "MSSQL_RETRY_INTERVAL_MAX";

    // Defaults per ADR-0004 and ADR-0015. Exposed as constants so tests and SqlExecutor
    // can reference the same source of truth instead of hardcoding literals.
    public const int DefaultRetryCount = 3;
    public const int DefaultRetryIntervalMin = 2;
    public const int DefaultRetryIntervalMax = 10;

    public const string CliConnectionString = "--connection-string";
    public const string CliAccessMode = "--access-mode";
    public const string CliQueryTimeout = "--query-timeout";
    public const string CliLogLevel = "--log-level";
    public const string CliValidate = "--validate";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// When true, the host opens a connection, runs SELECT 1, prints a result to stderr,
    /// and exits 0/1 — without starting the MCP stdio server. Pre-flight check (ticket 10).
    /// CLI-only: no env var, since validate is a one-shot operator action, not runtime config.
    /// </summary>
    public bool Validate { get; set; }
    public AccessMode AccessMode { get; set; } = AccessMode.Restricted;
    public int QueryTimeout { get; set; } = 30;
    public string LogLevel { get; set; } = "info";
    public string? LogFile { get; set; }
    public long MaxResultBytes { get; set; } = 10 * 1024 * 1024; // 10 MB per ADR-0003
    public int RetryCount { get; set; } = DefaultRetryCount;
    public int RetryIntervalMin { get; set; } = DefaultRetryIntervalMin;
    public int RetryIntervalMax { get; set; } = DefaultRetryIntervalMax;

    /// <summary>
    /// Parses args + env into a validated <see cref="MssqlMcpOptions"/>.
    /// Throws <see cref="InvalidOperationException"/> with a clear [startup] message on any invalid value.
    /// </summary>
    public static MssqlMcpOptions Parse(string[] args, System.Collections.IDictionary env)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);

        var options = new MssqlMcpOptions();

        // --validate is a boolean switch (no value). CLI-only — no env var, since validate
        // is a one-shot operator action, not runtime config (ADR-0015).
        options.Validate = HasCliFlag(args, CliValidate);

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

        // Retry interval min/max: env only. MSSQL_RETRY_INTERVAL sets min, MSSQL_RETRY_INTERVAL_MAX sets max.
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

        string? retryMaxRaw = GetEnv(env, EnvRetryIntervalMax);
        if (!string.IsNullOrWhiteSpace(retryMaxRaw))
        {
            if (!int.TryParse(retryMaxRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMax) || parsedMax < 0)
            {
                throw new InvalidOperationException(
                    $"[startup] Invalid retry interval max '{retryMaxRaw}'. Must be a non-negative integer.");
            }
            options.RetryIntervalMax = parsedMax;
        }

        ValidateRetryBounds(options);
        return options;
    }

    /// <summary>
    /// Cross-field validation for retry config that Microsoft.Data.SqlClient's
    /// <c>SqlRetryLogicOption</c> enforces at provider construction time. Failing fast
    /// at startup (ADR-0015) gives a clearer <c>[startup]</c> message than the runtime
    /// <see cref="ArgumentOutOfRangeException"/> SqlClient would throw later.
    /// </summary>
    private static void ValidateRetryBounds(MssqlMcpOptions options)
    {
        // SqlRetryLogicOption.NumberOfTries must be 1-60. RetryCount is retries-after-first,
        // so RetryCount+1 = NumberOfTries. RetryCount=60 → NumberOfTries=61 (invalid).
        const int MaxRetryCount = 59;
        if (options.RetryCount > MaxRetryCount)
        {
            throw new InvalidOperationException(
                $"[startup] Invalid retry count '{options.RetryCount}'. Must be 0-{MaxRetryCount} (Microsoft.Data.SqlClient allows 1-60 total attempts).");
        }

        // SqlRetryLogicOption enforces MaxTimeInterval ≤ 120s and MinTimeInterval < MaxTimeInterval (strict).
        const int MaxIntervalSeconds = 120;
        if (options.RetryIntervalMin > MaxIntervalSeconds)
        {
            throw new InvalidOperationException(
                $"[startup] Invalid retry interval min '{options.RetryIntervalMin}'. Must be 0-{MaxIntervalSeconds} seconds.");
        }
        if (options.RetryIntervalMax > MaxIntervalSeconds)
        {
            throw new InvalidOperationException(
                $"[startup] Invalid retry interval max '{options.RetryIntervalMax}'. Must be 0-{MaxIntervalSeconds} seconds.");
        }
        if (options.RetryIntervalMin >= options.RetryIntervalMax)
        {
            throw new InvalidOperationException(
                $"[startup] Retry interval min ({options.RetryIntervalMin}s) must be strictly less than max ({options.RetryIntervalMax}s).");
        }
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
    /// Returns true if the boolean <paramref name="flag"/> is present in argv.
    /// Accepts <c>--flag</c>, <c>--flag=true</c>, <c>--flag=false</c> (case-sensitive flag name).
    /// </summary>
    private static bool HasCliFlag(string[] args, string flag)
    {
        string eqForm = flag + "=";
        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (token.StartsWith(eqForm, StringComparison.Ordinal))
            {
                string value = token[eqForm.Length..];
                return bool.TryParse(value, out bool parsed) && parsed;
            }
            if (token == flag)
            {
                return true;
            }
        }
        return false;
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
