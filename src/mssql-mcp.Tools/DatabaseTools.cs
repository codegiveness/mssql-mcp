using System.ComponentModel;
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

    private const string ListSchemasCurrentDbSql =
        """
        SELECT name, schema_id FROM sys.schemas ORDER BY schema_id
        """;

    private const string ListObjectsSqlTemplate =
        """
        SELECT TOP (@limit) name, schema_name(schema_id) AS [schema], type_desc AS [type]
        FROM {0}sys.objects
        WHERE is_ms_shipped=0{1}{2}
        ORDER BY schema_name(schema_id), name
        """;

    private const string GetObjectDetailsLookupSqlTemplate =
        """
        SELECT type, object_id FROM {0}sys.objects
        WHERE schema_id=SCHEMA_ID(@schema) AND name=@name{1}
        """;

    private const string ColumnsSqlTemplate =
        """
        SELECT name, system_type_name, max_length, precision, scale, is_nullable, is_identity, ordinal_position
        FROM {0}sys.columns
        WHERE object_id = @objectId
        ORDER BY ordinal_position
        """;

    private const string ParametersSqlTemplate =
        """
        SELECT name, system_type_name, max_length, precision, scale, is_output, parameter_id, default_value
        FROM {0}sys.parameters
        WHERE object_id = @objectId
        ORDER BY parameter_id
        """;

    private const string IndexesSqlTemplate =
        """
        SELECT name, type_desc, is_unique, is_primary_key
        FROM {0}sys.indexes
        WHERE object_id = @objectId
        ORDER BY index_id
        """;

    private const string TriggersSqlTemplate =
        """
        SELECT name, type_desc
        FROM {0}sys.triggers
        WHERE parent_id = @objectId
        ORDER BY name
        """;

    private const int DefaultObjectLimit = 1000;
    private const int MaxObjectLimit = 5000;

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
            return ToolErrors.SqlErrorOrConnection(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[internal] list_databases unhandled exception: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return ToolErrors.Internal(ex);
        }

        _logger.LogInformation("[tool] list_databases returned {Count} databases", rows.Count);
        return ToolErrors.SuccessWithByteCap(rows, _options.MaxResultBytes, _logger);
    }

    /// <summary>
    /// Lists all schemas in the current or specified database, including system schemas.
    /// Sorted by schema_id (dbo first).
    /// </summary>
    [McpServerTool(Name = "list_schemas", Title = "List schemas", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists all schemas in the current or specified database, including system schemas. Sorted by schema_id (dbo first).")]
    public async Task<CallToolResult> ListSchemas(
        [Description("Database name. Defaults to the current database.")] string? database,
        CancellationToken ct)
    {
        _logger.LogInformation("[tool] list_schemas invoked (database={Database})", database ?? "<current>");

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

        string sql = string.IsNullOrEmpty(dbPrefix)
            ? ListSchemasCurrentDbSql
            : $"SELECT name, schema_id FROM {dbPrefix}sys.schemas ORDER BY schema_id";

        List<Dictionary<string, object?>> rows;
        try
        {
            rows = await _executor.ExecuteQueryAsync(sql, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("[timeout] list_schemas exceeded {Timeout}s command timeout", _options.QueryTimeout);
            return ToolErrors.Timeout(_options.QueryTimeout);
        }
        catch (SqlException ex)
        {
            _logger.LogError("[sql] list_schemas failed: {Message} (code {Number})", ex.Message, ex.Number);
            return ToolErrors.SqlErrorOrConnection(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[internal] list_schemas unhandled exception: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return ToolErrors.Internal(ex);
        }

        _logger.LogInformation("[tool] list_schemas returned {Count} schemas", rows.Count);
        return ToolErrors.SuccessWithByteCap(rows, _options.MaxResultBytes, _logger);
    }

    /// <summary>
    /// Lists user objects (excludes system-shipped) in the current or specified database.
    /// Filter by schema and type. Defaults to 1000 rows.
    /// </summary>
    [McpServerTool(Name = "list_objects", Title = "List objects", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists user objects (excludes system-shipped) in the current or specified database. Filter by schema and type. Defaults to 1000 rows.")]
    public async Task<CallToolResult> ListObjects(
        [Description("Database name. Defaults to current.")] string? database,
        [Description("Schema name to filter by. Null = all schemas.")] string? schema,
        [Description("Object type filter: TABLE, VIEW, PROCEDURE, or FUNCTION. Null = all types.")] string? type,
        [Description("Maximum rows to return (default 1000, max 5000).")] int? limit,
        CancellationToken ct)
    {
        _logger.LogInformation("[tool] list_objects invoked (database={Database}, schema={Schema}, type={Type}, limit={Limit})",
            database ?? "<current>", schema ?? "<all>", type ?? "<all>", limit);

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

        int clampedLimit = ClampLimit(limit, DefaultObjectLimit, MaxObjectLimit);
        string typeFilter = BuildTypeFilter(type);
        string schemaFilter = schema is null ? string.Empty : " AND schema_id=SCHEMA_ID(@schema)";

        string sql = string.Format(ListObjectsSqlTemplate, dbPrefix, schemaFilter, typeFilter);

        Dictionary<string, object> parameters = new() { ["limit"] = clampedLimit };
        if (schema is not null)
        {
            parameters["schema"] = schema;
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
            _logger.LogError("[timeout] list_objects exceeded {Timeout}s command timeout", _options.QueryTimeout);
            return ToolErrors.Timeout(_options.QueryTimeout);
        }
        catch (SqlException ex)
        {
            _logger.LogError("[sql] list_objects failed: {Message} (code {Number})", ex.Message, ex.Number);
            return ToolErrors.SqlErrorOrConnection(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[internal] list_objects unhandled exception: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return ToolErrors.Internal(ex);
        }

        List<object> payload = new(rows.Count + 1);
        if (rows.Count == clampedLimit)
        {
            payload.Add(new
            {
                truncated = true,
                returned = clampedLimit,
                note = "Results truncated. Refine schema/type filters or raise limit.",
            });
        }
        foreach (Dictionary<string, object?> row in rows)
        {
            payload.Add(row);
        }

        _logger.LogInformation("[tool] list_objects returned {Count} objects (truncated={Truncated})",
            rows.Count, rows.Count == clampedLimit);
        return ToolErrors.SuccessWithByteCap(payload, _options.MaxResultBytes, _logger);
    }

    /// <summary>
    /// Returns detailed metadata for a specific object: columns (tables/views), parameters
    /// (procedures/functions), indexes (tables), triggers. Returns OBJECT_NOT_FOUND if the
    /// object doesn't exist.
    /// </summary>
    [McpServerTool(Name = "get_object_details", Title = "Get object details", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Returns detailed metadata for a specific object: columns (tables/views), parameters (procedures/functions), indexes (tables), triggers. Returns OBJECT_NOT_FOUND if the object doesn't exist.")]
    public async Task<CallToolResult> GetObjectDetails(
        [Description("Database name. Defaults to current.")] string? database,
        [Description("Schema name (e.g. 'dbo').")] string schema,
        [Description("Object name (e.g. 'Orders').")] string name,
        [Description("Object type filter: TABLE, VIEW, PROCEDURE, or FUNCTION. Null = auto-detect.")] string? type,
        CancellationToken ct)
    {
        _logger.LogInformation("[tool] get_object_details invoked (database={Database}, schema={Schema}, name={Name}, type={Type})",
            database ?? "<current>", schema, name, type ?? "<auto>");

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

        string typeFilter = type is null ? string.Empty : BuildTypeFilter(type);
        string lookupSql = string.Format(GetObjectDetailsLookupSqlTemplate, dbPrefix, typeFilter);

        Dictionary<string, object> lookupParams = new() { ["schema"] = schema, ["name"] = name };

        List<Dictionary<string, object?>> lookupRows;
        List<Dictionary<string, object?>> detailRows = new();
        try
        {
            lookupRows = await _executor.ExecuteQueryAsync(lookupSql, lookupParams, ct).ConfigureAwait(false);

            if (lookupRows.Count == 0)
            {
                return ToolErrors.ObjectNotFoundError(database, schema, name, type);
            }

            string typeChar = lookupRows[0].TryGetValue("type", out object? t) && t is string tv ? tv : string.Empty;
            long objectId = lookupRows[0].TryGetValue("object_id", out object? oid) && oid is long l ? l : 0L;

            string columnsSql = string.Format(ColumnsSqlTemplate, dbPrefix);
            string parametersSql = string.Format(ParametersSqlTemplate, dbPrefix);
            string indexesSql = string.Format(IndexesSqlTemplate, dbPrefix);
            string triggersSql = string.Format(TriggersSqlTemplate, dbPrefix);

            Dictionary<string, object> detailParams = new() { ["objectId"] = objectId };

            if (typeChar == "U" || typeChar == "V")
            {
                List<Dictionary<string, object?>> cols = await _executor.ExecuteQueryAsync(columnsSql, detailParams, ct).ConfigureAwait(false);
                detailRows.AddRange(cols);
            }

            if (typeChar == "P" || typeChar == "PC" || typeChar == "FN" || typeChar == "IF" || typeChar == "TF" || typeChar == "FS" || typeChar == "FT")
            {
                List<Dictionary<string, object?>> pars = await _executor.ExecuteQueryAsync(parametersSql, detailParams, ct).ConfigureAwait(false);
                detailRows.AddRange(pars);
            }

            if (typeChar == "U")
            {
                List<Dictionary<string, object?>> idx = await _executor.ExecuteQueryAsync(indexesSql, detailParams, ct).ConfigureAwait(false);
                detailRows.AddRange(idx);

                List<Dictionary<string, object?>> trg = await _executor.ExecuteQueryAsync(triggersSql, detailParams, ct).ConfigureAwait(false);
                detailRows.AddRange(trg);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("[timeout] get_object_details exceeded {Timeout}s command timeout", _options.QueryTimeout);
            return ToolErrors.Timeout(_options.QueryTimeout);
        }
        catch (SqlException ex)
        {
            _logger.LogError("[sql] get_object_details failed: {Message} (code {Number})", ex.Message, ex.Number);
            return ToolErrors.SqlErrorOrConnection(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[internal] get_object_details unhandled exception: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return ToolErrors.Internal(ex);
        }

        _logger.LogInformation("[tool] get_object_details returned {Count} detail rows", detailRows.Count);
        return ToolErrors.SuccessWithByteCap(detailRows, _options.MaxResultBytes, _logger);
    }

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

    private static string BuildTypeFilter(string? type)
    {
        return type?.ToUpperInvariant() switch
        {
            "TABLE" => " AND type='U'",
            "VIEW" => " AND type='V'",
            "PROCEDURE" => " AND type IN ('P','PC')",
            "FUNCTION" => " AND type IN ('FN','IF','TF','FS','FT')",
            _ => string.Empty,
        };
    }
}
