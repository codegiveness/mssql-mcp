using System.Globalization;

namespace mssql_mcp.Core.Configuration;

/// <summary>
/// Resolved runtime configuration for the mssql-mcp server.
/// Precedence: CLI flag &gt; env var &gt; hardcoded default, except
/// MSSQL_CONNECTION_STRING wins over --connection-string (secrets live in env, not argv).
/// </summary>
public sealed class MssqlMcpOptions
{
    /// <summary>Environment variable name for the SQL Server connection string.</summary>
    public const string EnvConnectionString = "MSSQL_CONNECTION_STRING";
    /// <summary>Environment variable name for the access mode (restricted/unrestricted).</summary>
    public const string EnvAccessMode = "MSSQL_ACCESS_MODE";
    /// <summary>Environment variable name for the per-query command timeout in seconds.</summary>
    public const string EnvQueryTimeout = "MSSQL_QUERY_TIMEOUT";
    /// <summary>Environment variable name for the log level.</summary>
    public const string EnvLogLevel = "MSSQL_LOG_LEVEL";
    /// <summary>Environment variable name for the optional log file path.</summary>
    public const string EnvLogFile = "MSSQL_LOG_FILE";
    /// <summary>Environment variable name for the log file rotation byte threshold.</summary>
    public const string EnvLogFileMaxBytes = "MSSQL_LOG_FILE_MAX_BYTES";
    /// <summary>Environment variable name for the number of archived log files retained.</summary>
    public const string EnvLogFileMaxRolls = "MSSQL_LOG_FILE_MAX_ROLLS";
    /// <summary>Environment variable name for the result byte-size safety net.</summary>
    public const string EnvMaxResultBytes = "MSSQL_MAX_RESULT_BYTES";
    /// <summary>Environment variable name for the transient-failure retry count.</summary>
    public const string EnvRetryCount = "MSSQL_RETRY_COUNT";
    /// <summary>Environment variable name for the minimum transient retry backoff interval.</summary>
    public const string EnvRetryIntervalMin = "MSSQL_RETRY_INTERVAL";
    /// <summary>Environment variable name for the maximum transient retry backoff interval.</summary>
    public const string EnvRetryIntervalMax = "MSSQL_RETRY_INTERVAL_MAX";

    // Defaults per ADR-0004 and ADR-0015. Exposed as constants so tests and SqlExecutor
    // can reference the same source of truth instead of hardcoding literals.
    /// <summary>Default retry count after first attempt (per ADR-0004).</summary>
    public const int DefaultRetryCount = 3;
    /// <summary>Default minimum retry backoff in seconds (per ADR-0004).</summary>
    public const int DefaultRetryIntervalMin = 2;
    /// <summary>Default maximum retry backoff in seconds (per ADR-0004).</summary>
    public const int DefaultRetryIntervalMax = 10;

    // Default rotation thresholds per ADR-0030. 50 MB active file cap, 3 archived rolls.
    /// <summary>Default byte threshold for active log file rotation (50 MB, per ADR-0030).</summary>
    public const long DefaultLogFileMaxBytes = 50 * 1024 * 1024;
    /// <summary>Default number of archived log files retained (per ADR-0030).</summary>
    public const int DefaultLogFileMaxRolls = 3;

    /// <summary>CLI flag for the SQL Server connection string.</summary>
    public const string CliConnectionString = "--connection-string";
    /// <summary>CLI flag for the access mode.</summary>
    public const string CliAccessMode = "--access-mode";
    /// <summary>CLI flag for the per-query command timeout.</summary>
    public const string CliQueryTimeout = "--query-timeout";
    /// <summary>CLI flag for the log level.</summary>
    public const string CliLogLevel = "--log-level";
    /// <summary>CLI flag for the pre-flight connection validation.</summary>
    public const string CliValidate = "--validate";

    /// <summary>SQL Server connection string (env wins over CLI per ADR-0015).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// When true, the host opens a connection, runs SELECT 1, prints a result to stderr,
    /// and exits 0/1 — without starting the MCP stdio server. Pre-flight check (ticket 10).
    /// CLI-only: no env var, since validate is a one-shot operator action, not runtime config.
    /// </summary>
    public bool Validate { get; set; }
    /// <summary>Access mode: Restricted (default, read-only) or Unrestricted (opt-in, DML/DDL).</summary>
    public AccessMode AccessMode { get; set; } = AccessMode.Restricted;
    /// <summary>Per-query command timeout in seconds. 0 = unlimited (per ADR-0007).</summary>
    public int QueryTimeout { get; set; } = 30;
    /// <summary>Log level: trace, debug, info, warning, error, critical (per ADR-0011).</summary>
    public string LogLevel { get; set; } = "info";
    /// <summary>Optional file path for log output. Null = stderr only (per ADR-0011).</summary>
    public string? LogFile { get; set; }
    /// <summary>Byte threshold for active log file rotation (per ADR-0030). 0 disables rotation.</summary>
    public long LogFileMaxBytes { get; set; } = DefaultLogFileMaxBytes;
    /// <summary>Number of archived log files retained (per ADR-0030).</summary>
    public int LogFileMaxRolls { get; set; } = DefaultLogFileMaxRolls;
    /// <summary>Result byte-size safety net in bytes (per ADR-0003). 0 disables the cap.</summary>
    public long MaxResultBytes { get; set; } = 10 * 1024 * 1024; // 10 MB per ADR-0003
    /// <summary>Transient-failure retry count after first attempt (per ADR-0004).</summary>
    public int RetryCount { get; set; } = DefaultRetryCount;
    /// <summary>Minimum transient retry backoff in seconds (per ADR-0004).</summary>
    public int RetryIntervalMin { get; set; } = DefaultRetryIntervalMin;
    /// <summary>Maximum transient retry backoff in seconds (per ADR-0004).</summary>
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

        // Log file rotation: env only per ADR-0030. maxBytes=0 disables rotation entirely.
        string? maxBytesRaw = GetEnv(env, EnvLogFileMaxBytes);
        if (!string.IsNullOrWhiteSpace(maxBytesRaw))
        {
            if (!long.TryParse(maxBytesRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedMaxBytes) || parsedMaxBytes < 0)
            {
                throw new InvalidOperationException(
                    $"[startup] Invalid log file max bytes '{maxBytesRaw}'. Must be a non-negative integer.");
            }
            options.LogFileMaxBytes = parsedMaxBytes;
        }

        string? maxRollsRaw = GetEnv(env, EnvLogFileMaxRolls);
        if (!string.IsNullOrWhiteSpace(maxRollsRaw))
        {
            if (!int.TryParse(maxRollsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMaxRolls) || parsedMaxRolls < 0)
            {
                throw new InvalidOperationException(
                    $"[startup] Invalid log file max rolls '{maxRollsRaw}'. Must be a non-negative integer.");
            }
            options.LogFileMaxRolls = parsedMaxRolls;
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
