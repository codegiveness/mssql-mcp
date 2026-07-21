using System.ComponentModel;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;

namespace mssql_mcp.Tools;

/// <summary>
/// Discovery tools that enumerate SQL Server metadata (databases, schemas, objects).
/// </summary>
[McpServerToolType]
public sealed class DatabaseTools
{
    private const string ListDatabasesSql =
        """
        SELECT name, database_id, state_desc,
               CASE WHEN name = DB_NAME() THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS is_current
        FROM sys.databases
        WHERE database_id > 4 AND database_id < 32767
        ORDER BY is_current DESC, name
        """;

    private readonly ISqlExecutor _executor;
    private readonly MssqlMcpOptions _options;
    private readonly ILogger<DatabaseTools> _logger;

    public DatabaseTools(ISqlExecutor executor, IOptions<MssqlMcpOptions> options, ILogger<DatabaseTools> logger)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _executor = executor;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Lists all user databases on the SQL Server, marking the current one with is_current.
    /// Excludes system databases (master, tempdb, model, msdb) and mssqlsystemresource.
    /// </summary>
    [McpServerTool(Name = "list_databases", Title = "List databases", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists all user databases on the SQL Server, marking the current one with is_current. Excludes system databases (master, tempdb, model, msdb) and mssqlsystemresource.")]
    public async Task<CallToolResult> ListDatabases(CancellationToken ct)
    {
        _logger.LogInformation("[tool] list_databases invoked");
        List<Dictionary<string, object?>> rows;
        try
        {
            rows = await _executor.ExecuteQueryAsync(ListDatabasesSql, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client cancellation — rethrow, not a timeout.
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("[timeout] list_databases exceeded {Timeout}s command timeout", _options.QueryTimeout);
            return ToolErrors.Timeout(_options.QueryTimeout);
        }
        catch (SqlException ex)
        {
            _logger.LogError("[sql] list_databases failed: {Message} (code {Number}, severity {Severity})", ex.Message, ex.Number, ex.Class);
            return ToolErrors.SqlError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[internal] list_databases unhandled exception: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return ToolErrors.Internal(ex);
        }

        _logger.LogInformation("[tool] list_databases returned {Count} databases", rows.Count);
        string json = JsonSerializer.Serialize(rows, ToolErrors.JsonOptions);
        return ToolErrors.Success(json);
    }
}
