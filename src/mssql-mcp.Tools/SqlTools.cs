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
    /// Executes a T-SQL query in Restricted mode (read-only, validated by the Guard).
    /// Returns results as a JSON array of objects per ADR-0009. Only SELECT and WITH...SELECT
    /// statements are allowed in Restricted mode. Errors return structured JSON per ADR-0010
    /// with <see cref="CallToolResult.IsError"/> set to <c>true</c>.
    /// </summary>
    [McpServerTool(Name = "execute_sql", Title = "Execute SQL", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Executes a T-SQL query in Restricted mode (read-only, validated by the Guard). Returns results as a JSON array of objects. Only SELECT and WITH...SELECT statements are allowed.")]
    public async Task<CallToolResult> ExecuteSql(
        [Description("The T-SQL query to execute")] string sql,
        CancellationToken ct)
    {
        _logger.LogInformation("[tool] execute_sql invoked");

        // Determine the SQL to execute. In Restricted mode, the Guard returns the wrapped SQL
        // (sentinel + BEGIN TRAN / ROLLBACK) per ADR-0007. In Unrestricted mode, we skip the
        // Guard entirely per ADR-0006 and execute the raw SQL (no wrapper).
        string sqlToExecute;
        if (_options.AccessMode == AccessMode.Restricted)
        {
            GuardResult guardResult = _guard.Validate(sql);
            if (!guardResult.Accepted)
            {
                GuardRejection rejection = guardResult.Rejection
                    ?? new GuardRejection("non_select_statement", "[guard] Rejected with no reason.");
                return GuardRejectionError(rejection);
            }
            if (guardResult.WrappedSql is null)
            {
                // Defensive: Guard invariants guarantee WrappedSql on accept; treat absence as internal error.
                return ToolErrors.Internal(new InvalidOperationException("Guard accepted but WrappedSql was null."));
            }
            sqlToExecute = guardResult.WrappedSql;
        }
        else
        {
            sqlToExecute = sql;
            if (string.IsNullOrWhiteSpace(sqlToExecute))
            {
                // Mirror the Guard's empty-batch rejection for consistency in Unrestricted mode.
                return GuardRejectionError(new GuardRejection(
                    rule: "empty_batch",
                    detail: "[guard] No executable statement found."));
            }
        }

        List<Dictionary<string, object?>> rows;
        try
        {
            rows = await _executor.ExecuteQueryAsync(sqlToExecute, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The MCP client cancelled the request — NOT a command timeout. Rethrow so the
            // MCP framework can handle the cancellation gracefully. Do NOT return a TIMEOUT
            // error for a client-initiated cancel.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Command timeout (not client cancellation) — return TIMEOUT per ADR-0010.
            _logger.LogError("[timeout] execute_sql exceeded {Timeout}s command timeout", _options.QueryTimeout);
            return ToolErrors.Timeout(_options.QueryTimeout);
        }
        catch (SqlException ex)
        {
            _logger.LogError("[sql] execute_sql failed: {Message} (code {Number}, severity {Severity})", ex.Message, ex.Number, ex.Class);
            return ToolErrors.SqlError(ex);
        }
        catch (Exception ex)
        {
            // ADR-0010 INTERNAL: any other unhandled exception. Never include stack trace in
            // the agent response — full detail goes to logs per ADR-0011.
            _logger.LogError(ex, "[internal] execute_sql unhandled exception: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return ToolErrors.Internal(ex);
        }

        string json = JsonSerializer.Serialize(rows, ToolErrors.JsonOptions);
        _logger.LogInformation("[tool] execute_sql returned {Count} rows", rows.Count);
        return ToolErrors.Success(json);
    }

    private static CallToolResult GuardRejectionError(GuardRejection rejection)
    {
        // ADR-0010 GUARD_REJECTION shape.
        object payload = new
        {
            error = "GUARD_REJECTION",
            rule = rejection.Rule,
            detail = rejection.Detail,
            statement_type = rejection.StatementType,
            position = rejection.Line is null && rejection.Column is null
                ? null
                : new { line = rejection.Line, column = rejection.Column },
        };
        string json = JsonSerializer.Serialize(payload, ToolErrors.JsonOptions);
        return new CallToolResult
        {
            Content = new List<ContentBlock> { new TextContentBlock { Text = json } },
            IsError = true,
        };
    }
}
