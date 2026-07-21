using System.ComponentModel;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;

namespace mssql_mcp.Tools;

/// <summary>
/// Operations tools that query <c>sys.dm_*</c> DMVs: <c>analyze_indexes</c>,
/// <c>get_top_queries</c>, <c>analyze_db_health</c>. All read-only.
/// </summary>
/// <remarks>
/// Cross-DB safety (ADR-0016):
/// <list type="bullet">
/// <item><c>analyze_indexes</c> and <c>analyze_db_health</c> query database-scoped DMVs
/// (<c>{db}sys.dm_db_*</c>) — use <see cref="SqlHelpers.QuoteIdentifier"/> for the DB prefix.</item>
/// <item><c>get_top_queries</c> queries server-scoped DMVs (<c>sys.dm_exec_query_stats</c>) —
/// filter by <c>dbid = DB_ID(@database)</c> instead, do NOT use a DB prefix.</item>
/// </list>
/// </remarks>
[McpServerToolType]
public sealed class OpsTools
{
    private const int IndexResultLimit = 20;
    private const int DefaultQueryLimit = 10;
    private const int MaxQueryLimit = 100;
    private const int QueryTextMaxLength = 500;

    // VLF status thresholds (per ADR-0016 / ticket 06 spec).
    private const int VlfOkThreshold = 50;
    private const int VlfCriticalThreshold = 1000;

    // ---------- analyze_indexes (workload-wide) ----------
    private const string MissingIndexWorkloadSqlTemplate =
        """
        SELECT TOP (20)
            mid.statement AS [object],
            mid.equality_columns,
            mid.inequality_columns,
            mid.included_columns,
            migs.user_seeks,
            migs.user_scans,
            migs.avg_user_impact,
            migs.avg_total_user_cost,
            migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans) AS improvement_measure
        FROM {0}sys.dm_db_missing_index_details mid
        JOIN {0}sys.dm_db_missing_index_groups mig ON mid.index_handle = mig.index_handle
        JOIN {0}sys.dm_db_missing_index_group_stats migs ON mig.index_group_handle = migs.group_handle
        ORDER BY improvement_measure DESC
        """;

    // ---------- analyze_indexes (per-query) ----------
    // Per ADR-0016: filter to a plan_handle by joining sys.dm_exec_query_stats + sys.dm_exec_sql_text
    // against the user-supplied query text, then JOIN missing-index DMVs to
    // sys.dm_db_missing_index_group_stats_query on query_plan_hash.
    private const string MissingIndexPerQuerySqlTemplate =
        """
        SELECT TOP (20)
            mid.statement AS [object],
            mid.equality_columns,
            mid.inequality_columns,
            mid.included_columns,
            migsqs.user_seeks,
            migsqs.user_scans,
            migsqs.avg_user_impact,
            migsqs.avg_total_user_cost,
            migsqs.avg_total_user_cost * migsqs.avg_user_impact * (migsqs.user_seeks + migsqs.user_scans) AS improvement_measure
        FROM {0}sys.dm_db_missing_index_details mid
        JOIN {0}sys.dm_db_missing_index_groups mig ON mid.index_handle = mig.index_handle
        JOIN {0}sys.dm_db_missing_index_group_stats_query migsqs ON mig.index_group_handle = migsqs.group_handle
        JOIN sys.dm_exec_query_stats qs ON migsqs.query_plan_hash = qs.query_plan_hash
        CROSS APPLY sys.dm_exec_sql_text(qs.plan_handle) st
        WHERE st.text LIKE '%' + @query + '%'
        ORDER BY improvement_measure DESC
        """;

    // ---------- get_top_queries ----------
    private const string TopQueriesSqlTemplate =
        """
        SELECT TOP (@limit)
            SUBSTRING(st.text, (qs.statement_start_offset/2)+1,
                (CASE qs.statement_end_offset
                    WHEN -1 THEN DATALENGTH(st.text)
                    ELSE qs.statement_end_offset
                END - qs.statement_start_offset)/2 + 1) AS query_text,
            qs.execution_count,
            qs.total_worker_time,
            qs.total_elapsed_time,
            qs.total_logical_reads,
            qs.plan_generation_num,
            qs.creation_time
        FROM sys.dm_exec_query_stats qs
        CROSS APPLY sys.dm_exec_sql_text(qs.plan_handle) st
        WHERE st.dbid = {0}
        ORDER BY {1}
        """;

    // ---------- analyze_db_health ----------
    // 1. Database size + log size (database-scoped — use QuoteIdentifier prefix).
    private const string DatabaseSizeSqlTemplate =
        """
        SELECT SUM(size * 8 / 1024) AS size_mb,
               SUM(CASE WHEN type = 1 THEN size * 8 / 1024 ELSE 0 END) AS log_mb
        FROM {0}sys.database_files
        """;

    // 2. VLF count (database-scoped — sys.dm_db_log_info takes a database_id argument).
    private const string VlfCountSqlTemplate =
        """
        SELECT COUNT(*) AS vlf_count FROM {0}sys.dm_db_log_info({1})
        """;

    // 3. Index fragmentation summary (SAMPLED mode — NOT DETAILED per ADR-0016).
    //    OBJECT_NAME uses database_id arg to resolve names in the target DB (Oracle C1).
    private const string IndexFragmentationSqlTemplate =
        """
        SELECT COUNT(*) AS total_indexes,
               SUM(CASE WHEN ips.avg_fragmentation_in_percent > 30 THEN 1 ELSE 0 END) AS fragmented_gt_30pct,
               MAX(ips.avg_fragmentation_in_percent) AS max_fragmentation,
               (SELECT TOP 1 OBJECT_NAME(ips.object_id, {1}) + ' (' + CAST(CAST(ips.avg_fragmentation_in_percent AS int) AS varchar) + '%)'
                FROM {0}sys.dm_db_index_physical_stats({1}, NULL, NULL, NULL, 'SAMPLED') ips
                WHERE ips.avg_fragmentation_in_percent > 30
                ORDER BY ips.avg_fragmentation_in_percent DESC) AS worst
        FROM {0}sys.dm_db_index_physical_stats({1}, NULL, NULL, NULL, 'SAMPLED') ips
        """;

    // 4. Statistics staleness (database-scoped — uses sys.stats which is DB-scoped).
    private const string StatsStalenessSqlTemplate =
        """
        SELECT COUNT(*) AS total_stats,
               SUM(CASE WHEN DATEDIFF(day, STATS_DATE(object_id, stats_id), GETDATE()) > 7 THEN 1 ELSE 0 END) AS stale_gt_7d,
               MAX(DATEDIFF(day, STATS_DATE(object_id, stats_id), GETDATE())) AS max_staleness_days
        FROM {0}sys.stats
        WHERE auto_created = 0 OR user_created = 1
        """;

    // 5. Active blocking (server-scoped — no DB prefix).
    private const string BlockingSql =
        """
        SELECT COUNT(*) AS blocked_sessions
        FROM sys.dm_exec_requests WHERE blocking_session_id <> 0
        """;

    private readonly ISqlExecutor _executor;
    private readonly MssqlMcpOptions _options;
    private readonly ILogger<OpsTools> _logger;

    public OpsTools(ISqlExecutor executor, IOptions<MssqlMcpOptions> options, ILogger<OpsTools> logger)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _executor = executor;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns missing index recommendations from <c>sys.dm_db_missing_index_*</c> DMVs.
    /// If <paramref name="query"/> is provided, analyzes per-query missing indexes (filters
    /// by plan handle via <c>sys.dm_exec_query_stats</c>). If omitted, returns workload-wide
    /// missing indexes. Limited to top 20 by <c>improvement_measure</c>.
    /// </summary>
    [McpServerTool(Name = "analyze_indexes", Title = "Analyze indexes", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Returns missing index recommendations. If query is provided, analyzes per-query missing indexes. If omitted, returns workload-wide missing indexes.")]
    public async Task<CallToolResult> AnalyzeIndexes(
        [Description("Database name. Defaults to current.")] string? database,
        [Description("SQL query to analyze for missing indexes. Null = workload-wide analysis.")] string? query,
        CancellationToken ct)
    {
        _logger.LogInformation("[tool] analyze_indexes invoked (database={Database}, query={Query})",
            database ?? "<current>", query is null ? "<workload>" : "<per-query>");

        string dbPrefix;
        if (database is not null)
        {
            DatabaseValidationResult? validation = await TryValidateDatabaseAsync(database, ct).ConfigureAwait(false);
            if (validation is null)
            {
                return ToolErrors.Timeout(_options.QueryTimeout);
            }
            if (validation is { Valid: false, Error: not null })
            {
                return ToolErrors.ConnectionError(validation.Error);
            }
            dbPrefix = SqlHelpers.QuoteIdentifier(database) + ".";
        }
        else
        {
            dbPrefix = string.Empty;
        }

        string sql;
        Dictionary<string, object> parameters = new();
        if (!string.IsNullOrWhiteSpace(query))
        {
            sql = string.Format(CultureInfo.InvariantCulture, MissingIndexPerQuerySqlTemplate, dbPrefix);
            parameters["query"] = query;
        }
        else
        {
            sql = string.Format(CultureInfo.InvariantCulture, MissingIndexWorkloadSqlTemplate, dbPrefix);
        }

        List<Dictionary<string, object?>> rows;
        try
        {
            rows = await _executor.ExecuteQueryAsync(sql, parameters, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("[timeout] analyze_indexes exceeded {Timeout}s command timeout", _options.QueryTimeout);
            return ToolErrors.Timeout(_options.QueryTimeout);
        }
        catch (SqlException ex)
        {
            _logger.LogError("[sql] analyze_indexes failed: {Message} (code {Number})", ex.Message, ex.Number);
            return ToolErrors.SqlErrorOrConnection(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[internal] analyze_indexes unhandled exception: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return ToolErrors.Internal(ex);
        }

        // SQL TOP (20) limits the rows; truncate to the cap for defense-in-depth in case the
        // query is ever changed to remove TOP (ADR-0016: top 20 by improvement_measure).
        if (rows.Count > IndexResultLimit)
        {
            rows = rows.GetRange(0, IndexResultLimit);
        }

        _logger.LogInformation("[tool] analyze_indexes returned {Count} missing indexes", rows.Count);
        return ToolErrors.SuccessWithByteCap(rows, _options.MaxResultBytes, _logger);
    }

    /// <summary>
    /// Returns the most expensive queries by CPU, duration, or reads using
    /// <c>sys.dm_exec_query_stats</c>. Does NOT require Query Store.
    /// </summary>
    [McpServerTool(Name = "get_top_queries", Title = "Get top queries", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Returns the most expensive queries by CPU, duration, or reads. Uses sys.dm_exec_query_stats. Requires no Query Store.")]
    public async Task<CallToolResult> GetTopQueries(
        [Description("Database name. Defaults to current.")] string? database,
        [Description("Sort order: avg_cpu (default), total_cpu, avg_duration, total_duration, total_logical_reads, execution_count.")] string? order_by,
        [Description("Maximum queries to return (default 10, max 100).")] int? limit,
        CancellationToken ct)
    {
        _logger.LogInformation("[tool] get_top_queries invoked (database={Database}, order_by={OrderBy}, limit={Limit})",
            database ?? "<current>", order_by ?? "<avg_cpu>", limit);

        // Server-scoped DMVs: filter by dbid via DB_ID(), not by DB prefix.
        // Still validate the database name for safety (confirm it exists and is online).
        if (database is not null)
        {
            DatabaseValidationResult? validation = await TryValidateDatabaseAsync(database, ct).ConfigureAwait(false);
            if (validation is null)
            {
                return ToolErrors.Timeout(_options.QueryTimeout);
            }
            if (validation is { Valid: false, Error: not null })
            {
                return ToolErrors.ConnectionError(validation.Error);
            }
        }

        int clampedLimit = ClampLimit(limit, DefaultQueryLimit, MaxQueryLimit);
        string orderByClause = MapOrderBy(order_by);
        string dbIdExpr = database is null ? "DB_ID()" : "DB_ID(@database)";

        string sql = string.Format(CultureInfo.InvariantCulture, TopQueriesSqlTemplate, dbIdExpr, orderByClause);

        Dictionary<string, object> parameters = new() { ["limit"] = clampedLimit };
        if (database is not null)
        {
            parameters["database"] = database;
        }

        List<Dictionary<string, object?>> rows;
        try
        {
            rows = await _executor.ExecuteQueryAsync(sql, parameters, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("[timeout] get_top_queries exceeded {Timeout}s command timeout", _options.QueryTimeout);
            return ToolErrors.Timeout(_options.QueryTimeout);
        }
        catch (SqlException ex)
        {
            _logger.LogError("[sql] get_top_queries failed: {Message} (code {Number})", ex.Message, ex.Number);
            return ToolErrors.SqlErrorOrConnection(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[internal] get_top_queries unhandled exception: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return ToolErrors.Internal(ex);
        }

        // Truncate query_text to first 500 chars in C# (not SQL — keep SQL simple per ADR-0016).
        foreach (Dictionary<string, object?> row in rows)
        {
            if (row.TryGetValue("query_text", out object? qt) && qt is string text && text.Length > QueryTextMaxLength)
            {
                row["query_text"] = text.Substring(0, QueryTextMaxLength);
            }
        }

        _logger.LogInformation("[tool] get_top_queries returned {Count} queries", rows.Count);
        return ToolErrors.SuccessWithByteCap(rows, _options.MaxResultBytes, _logger);
    }

    /// <summary>
    /// Returns summary-level health checks: database size, VLF count, index fragmentation
    /// (SAMPLED mode), statistics staleness, active blocking. Drill down with
    /// <c>execute_sql</c> for details.
    /// </summary>
    [McpServerTool(Name = "analyze_db_health", Title = "Analyze database health", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Returns summary-level health checks: database size, VLF count, index fragmentation (SAMPLED mode), statistics staleness, active blocking. Drill down with execute_sql for details.")]
    public async Task<CallToolResult> AnalyzeDbHealth(
        [Description("Database name. Defaults to current.")] string? database,
        CancellationToken ct)
    {
        _logger.LogInformation("[tool] analyze_db_health invoked (database={Database})", database ?? "<current>");

        string dbPrefix;
        string dbIdExpr;
        if (database is not null)
        {
            DatabaseValidationResult? validation = await TryValidateDatabaseAsync(database, ct).ConfigureAwait(false);
            if (validation is null)
            {
                return ToolErrors.Timeout(_options.QueryTimeout);
            }
            if (validation is { Valid: false, Error: not null })
            {
                return ToolErrors.ConnectionError(validation.Error);
            }
            dbPrefix = SqlHelpers.QuoteIdentifier(database) + ".";
            dbIdExpr = "DB_ID(@database)";
        }
        else
        {
            dbPrefix = string.Empty;
            dbIdExpr = "DB_ID()";
        }

        List<object> summaries = new(capacity: 5);

        try
        {
            // 1. Database size + log size.
            string sizeSql = string.Format(CultureInfo.InvariantCulture, DatabaseSizeSqlTemplate, dbPrefix);
            List<Dictionary<string, object?>> sizeRows = await _executor.ExecuteQueryAsync(sizeSql, null, ct).ConfigureAwait(false);
            summaries.Add(BuildSizeSummary(sizeRows));

            // 2. VLF count.
            string vlfSql = string.Format(CultureInfo.InvariantCulture, VlfCountSqlTemplate, dbPrefix, dbIdExpr);
            Dictionary<string, object> vlfParams = new();
            if (database is not null)
            {
                vlfParams["database"] = database;
            }
            List<Dictionary<string, object?>> vlfRows = await _executor.ExecuteQueryAsync(vlfSql, vlfParams, ct).ConfigureAwait(false);
            summaries.Add(BuildVlfSummary(vlfRows));

            // 3. Index fragmentation (SAMPLED mode).
            string fragSql = string.Format(CultureInfo.InvariantCulture, IndexFragmentationSqlTemplate, dbPrefix, dbIdExpr);
            Dictionary<string, object> fragParams = new();
            if (database is not null)
            {
                fragParams["database"] = database;
            }
            List<Dictionary<string, object?>> fragRows = await _executor.ExecuteQueryAsync(fragSql, fragParams, ct).ConfigureAwait(false);
            summaries.Add(BuildFragmentationSummary(fragRows));

            // 4. Statistics staleness.
            string statsSql = string.Format(CultureInfo.InvariantCulture, StatsStalenessSqlTemplate, dbPrefix);
            List<Dictionary<string, object?>> statsRows = await _executor.ExecuteQueryAsync(statsSql, null, ct).ConfigureAwait(false);
            summaries.Add(BuildStatsSummary(statsRows));

            // 5. Active blocking (server-scoped — no DB prefix).
            List<Dictionary<string, object?>> blockRows = await _executor.ExecuteQueryAsync(BlockingSql, null, ct).ConfigureAwait(false);
            summaries.Add(BuildBlockingSummary(blockRows));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("[timeout] analyze_db_health exceeded {Timeout}s command timeout", _options.QueryTimeout);
            return ToolErrors.Timeout(_options.QueryTimeout);
        }
        catch (SqlException ex)
        {
            _logger.LogError("[sql] analyze_db_health failed: {Message} (code {Number})", ex.Message, ex.Number);
            return ToolErrors.SqlErrorOrConnection(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[internal] analyze_db_health unhandled exception: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return ToolErrors.Internal(ex);
        }

        _logger.LogInformation("[tool] analyze_db_health returned {Count} checks", summaries.Count);
        return ToolErrors.SuccessWithByteCap(summaries, _options.MaxResultBytes, _logger);
    }

    // ---------- Helpers ----------

    private async Task<DatabaseValidationResult?> TryValidateDatabaseAsync(string database, CancellationToken ct)
    {
        try
        {
            return await SqlHelpers.ValidateDatabaseAsync(_executor, database, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static int ClampLimit(int? limit, int defaultLimit, int maxLimit)
    {
        if (limit is null)
        {
            return defaultLimit;
        }
        if (limit < 1)
        {
            return 1;
        }
        if (limit > maxLimit)
        {
            return maxLimit;
        }
        return limit.Value;
    }

    private static string MapOrderBy(string? order_by)
    {
        return (order_by ?? string.Empty).ToLowerInvariant() switch
        {
            "total_cpu" => "total_worker_time DESC",
            "avg_duration" => "total_elapsed_time / execution_count DESC",
            "total_duration" => "total_elapsed_time DESC",
            "total_logical_reads" => "total_logical_reads DESC",
            "execution_count" => "execution_count DESC",
            // Default: avg_cpu
            _ => "total_worker_time / execution_count DESC",
        };
    }

    private static string VlfStatus(int count) => count switch
    {
        < VlfOkThreshold => "ok",
        <= VlfCriticalThreshold => "warning",
        _ => "critical",
    };

    private static object BuildSizeSummary(List<Dictionary<string, object?>> rows)
    {
        long sizeMb = 0L;
        long logMb = 0L;
        if (rows.Count > 0)
        {
            Dictionary<string, object?> row = rows[0];
            sizeMb = AsLong(row, "size_mb");
            logMb = AsLong(row, "log_mb");
        }
        return new
        {
            check = "database_size",
            size_mb = sizeMb,
            log_mb = logMb,
        };
    }

    private static object BuildVlfSummary(List<Dictionary<string, object?>> rows)
    {
        int count = 0;
        if (rows.Count > 0)
        {
            count = AsInt(rows[0], "vlf_count");
        }
        return new
        {
            check = "vlf_count",
            count,
            status = VlfStatus(count),
        };
    }

    private static object BuildFragmentationSummary(List<Dictionary<string, object?>> rows)
    {
        long totalIndexes = 0L;
        long fragmentedGt30 = 0L;
        double maxFrag = 0.0;
        string? worst = null;
        if (rows.Count > 0)
        {
            Dictionary<string, object?> row = rows[0];
            totalIndexes = AsLong(row, "total_indexes");
            fragmentedGt30 = AsLong(row, "fragmented_gt_30pct");
            maxFrag = AsDouble(row, "max_fragmentation");
            worst = row.TryGetValue("worst", out object? w) ? w as string : null;
        }
        return new
        {
            check = "index_fragmentation",
            total_indexes = totalIndexes,
            fragmented_gt_30pct = fragmentedGt30,
            max_fragmentation = maxFrag,
            worst,
        };
    }

    private static object BuildStatsSummary(List<Dictionary<string, object?>> rows)
    {
        long totalStats = 0L;
        long staleGt7d = 0L;
        int maxStalenessDays = 0;
        if (rows.Count > 0)
        {
            Dictionary<string, object?> row = rows[0];
            totalStats = AsLong(row, "total_stats");
            staleGt7d = AsLong(row, "stale_gt_7d");
            maxStalenessDays = AsInt(row, "max_staleness_days");
        }
        return new
        {
            check = "stats_staleness",
            total_stats = totalStats,
            stale_gt_7d = staleGt7d,
            oldest_days = maxStalenessDays,
        };
    }

    private static object BuildBlockingSummary(List<Dictionary<string, object?>> rows)
    {
        int blockedSessions = 0;
        if (rows.Count > 0)
        {
            blockedSessions = AsInt(rows[0], "blocked_sessions");
        }
        return new
        {
            check = "blocking",
            blocked_sessions = blockedSessions,
        };
    }

    private static long AsLong(Dictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out object? v) && v is not null)
        {
            return v switch
            {
                long l => l,
                int i => i,
                _ => long.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0L,
            };
        }
        return 0L;
    }

    private static int AsInt(Dictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out object? v) && v is not null)
        {
            return v switch
            {
                int i => i,
                long l => (int)l,
                _ => int.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0,
            };
        }
        return 0;
    }

    private static double AsDouble(Dictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out object? v) && v is not null)
        {
            return v switch
            {
                double d => d,
                float f => f,
                decimal dec => (double)dec,
                int i => i,
                long l => l,
                _ => double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0.0,
            };
        }
        return 0.0;
    }
}
