using System.ComponentModel;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;

namespace mssql_mcp.Tools;

/// <summary>
/// SQL execution tools (the <c>execute_sql</c> surface). In Restricted mode, the Guard
/// validates the SQL (AST allowlist + transaction wrapper per ADR-0006 / ADR-0007).
/// Errors are returned as <see cref="CallToolResult"/> with <see cref="CallToolResult.IsError"/>
/// set to <c>true</c> and a structured JSON envelope in the TextContent body per ADR-0010.
/// </summary>
[McpServerToolType]
public sealed class SqlTools
{
    private readonly ISqlExecutor _executor;
    private readonly IGuard _guard;
    private readonly MssqlMcpOptions _options;
    private readonly ILogger<SqlTools> _logger;

    public SqlTools(ISqlExecutor executor, IGuard guard, IOptions<MssqlMcpOptions> options, ILogger<SqlTools> logger)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _executor = executor;
        _guard = guard;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Executes a T-SQL query. In Restricted mode the Guard validates the SQL (AST allowlist +
    /// transaction wrapper per ADR-0006 / ADR-0007) and only SELECT/WITH is permitted. In
    /// Unrestricted mode the Guard is bypassed, DML/DDL is permitted, queries commit immediately
    /// (no transaction wrapper), and DML/DDL returns ADR-0009 status objects instead of a rowset.
    /// Errors return structured JSON per ADR-0010 with <see cref="CallToolResult.IsError"/> set.
    /// </summary>
    [McpServerTool(Name = "execute_sql", Title = "Execute SQL", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Executes a T-SQL query. In Restricted mode, only SELECT/WITH statements are allowed (Guard-validated, read-only transaction). In Unrestricted mode, DML/DDL is permitted, commits immediately (no transaction wrapper), and returns status objects with rows_affected (DML) or the affected object name (DDL). Returns rows as a JSON array of objects per ADR-0009.")]
    public async Task<CallToolResult> ExecuteSql(
        [Description("The T-SQL query to execute")] string sql,
        CancellationToken ct)
    {
        _logger.LogInformation("[tool] execute_sql invoked");

        // Restricted path: Guard returns wrapped SQL (sentinel + BEGIN TRAN / ROLLBACK per ADR-0007).
        if (_options.AccessMode == AccessMode.Restricted)
        {
            GuardResult guardResult = _guard.Validate(sql);
            if (!guardResult.Accepted)
            {
                GuardRejection rejection = guardResult.Rejection
                    ?? new GuardRejection("non_select_statement", "[guard] Rejected with no reason.");
                return ToolErrors.GuardRejection(rejection);
            }
            if (guardResult.WrappedSql is null)
            {
                // Defensive: Guard invariants guarantee WrappedSql on accept; treat absence as internal error.
                return ToolErrors.Internal(new InvalidOperationException("Guard accepted but WrappedSql was null."));
            }
            return await ExecuteQueryAndSerialize(guardResult.WrappedSql, ct).ConfigureAwait(false);
        }

        // Unrestricted path: bypass the Guard entirely (ADR-0006). No transaction wrapper —
        // queries commit immediately. Empty input is still rejected for consistency.
        if (string.IsNullOrWhiteSpace(sql))
        {
            return ToolErrors.GuardRejection(new GuardRejection(
                rule: "empty_batch",
                detail: "[guard] No executable statement found."));
        }

        IReadOnlyList<StatementInfo> statements = StatementClassifier.Classify(sql);

        // Security: refuse to execute unclassifiable SQL — parser failure means we cannot
        // determine the statement type, and executing unknown SQL is unsafe.
        if (statements.Count == 0)
        {
            return ToolErrors.GuardRejection(new GuardRejection(
                rule: "parse_error",
                detail: "[guard] Statement classifier could not parse the input. Refusing to execute unclassifiable SQL."));
        }

        bool hasNonSelect = false;
        foreach (StatementInfo s in statements)
        {
            if (s.StatementType != "SELECT")
            {
                hasNonSelect = true;
                break;
            }
        }

        if (!hasNonSelect)
        {
            return await ExecuteQueryAndSerialize(sql, ct).ConfigureAwait(false);
        }

        return await ExecuteNonQueryAndSerialize(sql, statements, ct).ConfigureAwait(false);
    }

    private async Task<CallToolResult> ExecuteQueryAndSerialize(string sql, CancellationToken ct)
    {
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
            _logger.LogError("[timeout] execute_sql exceeded {Timeout}s command timeout", _options.QueryTimeout);
            return ToolErrors.Timeout(_options.QueryTimeout);
        }
        catch (SqlException ex)
        {
            _logger.LogError("[sql] execute_sql failed: {Message} (code {Number}, severity {Severity})", ex.Message, ex.Number, ex.Class);
            return ToolErrors.SqlErrorOrConnection(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[internal] execute_sql unhandled exception: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return ToolErrors.Internal(ex);
        }

        _logger.LogInformation("[tool] execute_sql returned {Count} rows", rows.Count);
        return ToolErrors.SuccessWithByteCap(rows, _options.MaxResultBytes, _logger);
    }

    private async Task<CallToolResult> ExecuteNonQueryAndSerialize(
        string sql,
        IReadOnlyList<StatementInfo> statements,
        CancellationToken ct)
    {
        int rowsAffected;
        try
        {
            rowsAffected = await _executor.ExecuteNonQueryAsync(sql, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("[timeout] execute_sql exceeded {Timeout}s command timeout", _options.QueryTimeout);
            return ToolErrors.Timeout(_options.QueryTimeout);
        }
        catch (SqlException ex)
        {
            _logger.LogError("[sql] execute_sql failed: {Message} (code {Number}, severity {Severity})", ex.Message, ex.Number, ex.Class);
            return ToolErrors.SqlErrorOrConnection(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[internal] execute_sql unhandled exception: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return ToolErrors.Internal(ex);
        }

        // Build one status object per classified statement per ADR-0009. For a single-statement
        // batch, the rows-affected count maps directly. For multi-statement batches, SqlClient
        // returns the cumulative count across the whole batch — per-statement attribution isn't
        // reliably available without executing statements one at a time, so each status object
        // reports -1 (SQL Server's "not reported" sentinel) when more than one statement ran.
        List<object> statusObjects = new(capacity: statements.Count);
        bool single = statements.Count == 1;
        foreach (StatementInfo s in statements)
        {
            statusObjects.Add(BuildStatusObject(s, single ? rowsAffected : -1));
        }

        string json = JsonSerializer.Serialize(statusObjects, ToolErrors.JsonOptions);
        _logger.LogInformation("[tool] execute_sql (unrestricted) returned {Count} status objects", statusObjects.Count);
        return ToolErrors.Success(json);
    }

    private static object BuildStatusObject(StatementInfo info, int rowsAffected)
    {
        // ADR-0009 shape: DML carries rows_affected; DDL carries the object name. SELECT inside
        // a mixed batch is reported as statement_type=SELECT without rows_affected.
        bool isDml = info.StatementType is "INSERT" or "UPDATE" or "DELETE" or "MERGE"
            or "BULK_INSERT" or "TRUNCATE_TABLE";
        bool isDdl = !isDml && info.StatementType != "SELECT" && info.ObjectName is not null;

        if (isDml)
        {
            return new
            {
                result = "success",
                statement_type = info.StatementType,
                rows_affected = rowsAffected,
            };
        }
        if (isDdl)
        {
            return new
            {
                result = "success",
                statement_type = info.StatementType,
                @object = info.ObjectName,
            };
        }
        // SELECT in a mixed batch, or UNKNOWN statement type with no object name.
        return new
        {
            result = "success",
            statement_type = info.StatementType,
            rows_affected = rowsAffected,
        };
    }
}
