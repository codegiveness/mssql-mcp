using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core.Guard;

namespace mssql_mcp.Tools;

/// <summary>
/// Shared error-response helpers for all MCP tools. Produces <see cref="CallToolResult"/>
/// with structured JSON per ADR-0010. Used by <see cref="SqlTools"/>, <see cref="DatabaseTools"/>,
/// and future tool classes to avoid drift.
/// </summary>
internal static class ToolErrors
{
    /// <summary>
    /// Transient SQL Server error numbers per Microsoft's documented list. Transient
    /// <see cref="SqlException"/> after retries exhausted returns CONNECTION; non-transient
    /// returns SQL. Retry logic itself is ticket 08d — this classification only routes
    /// the final error to the right ADR-0010 class.
    /// </summary>
    /// <remarks>
    /// Source: <see href="https://learn.microsoft.com/azure/azure-sql/database/troubleshoot-common-errors-issues#transient-errors">SQL Server / SQL DB transient errors</see>.
    /// </remarks>
    private static readonly HashSet<int> TransientErrorNumbers = new()
    {
        4060, 40197, 40501, 40613, 41839, 49918, 49919, 49920, 11001,
    };

    /// <summary>
    /// Shared JSON serializer options. Does NOT use <see cref="JsonIgnoreCondition.WhenWritingNull"/>
    /// because ADR-0009 requires SQL NULL to appear as JSON <c>null</c> in row objects, not be omitted.
    /// Error payloads with null fields (e.g. <c>procedure</c>) are also serialized as <c>null</c>
    /// per ADR-0010.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private static CallToolResult Text(string json, bool isError)
    {
        return new CallToolResult
        {
            Content = new List<ContentBlock> { new TextContentBlock { Text = json } },
            IsError = isError,
        };
    }

    // ---------- Success ----------

    public static CallToolResult Success(string json) => Text(json, isError: false);

    // ---------- ADR-0010 error classes ----------

    /// <summary>
    /// ADR-0010 GUARD_REJECTION class. Includes <see cref="GuardRejection.Rule"/> so agents
    /// can self-correct precisely (e.g. <c>non_select_statement</c>, <c>select_into</c>,
    /// <c>openrowset</c>, <c>parse_error</c>, <c>empty_batch</c>).
    /// </summary>
    public static CallToolResult GuardRejection(GuardRejection rejection)
    {
        // statement_type is "" when unknown (e.g. parse_error, empty_batch) per ADR-0010.
        // position is null when both Line and Column are null; otherwise {line, column}.
        object payload = new
        {
            error = "GUARD_REJECTION",
            rule = rejection.Rule,
            detail = rejection.Detail,
            statement_type = rejection.StatementType ?? string.Empty,
            position = rejection.Line is null && rejection.Column is null
                ? null
                : new { line = rejection.Line, column = rejection.Column },
        };
        return Text(JsonSerializer.Serialize(payload, JsonOptions), isError: true);
    }

    public static CallToolResult Timeout(int timeoutSeconds)
    {
        int timeoutMs = timeoutSeconds * 1000;
        object payload = new
        {
            error = "TIMEOUT",
            timeout_ms = timeoutMs,
            detail = $"Query exceeded {timeoutSeconds}s command timeout",
        };
        return Text(JsonSerializer.Serialize(payload, JsonOptions), isError: true);
    }

    public static CallToolResult SqlError(SqlException ex)
    {
        // ADR-0010 SQL shape. SqlException may carry multiple errors — the first is the most actionable.
        SqlError? first = ex.Errors.Count > 0 ? ex.Errors[0] : null;
        int number = first?.Number ?? ex.Number;
        byte severity = first?.Class ?? ex.Class;
        int line = first?.LineNumber ?? ex.LineNumber;
        string? procedure = first?.Procedure ?? ex.Procedure;
        object payload = new
        {
            error = "SQL",
            code = $"SQL{number}",
            message = ex.Message,
            severity = severity,
            line = line,
            procedure = procedure,
        };
        return Text(JsonSerializer.Serialize(payload, JsonOptions), isError: true);
    }

    /// <summary>
    /// Classifies a <see cref="SqlException"/> and routes to the right ADR-0010 error class:
    /// transient errors → <see cref="ConnectionError(string)"/> (retries exhausted per ADR-0010),
    /// non-transient → <see cref="SqlError(SqlException)"/>. Severity-25 errors are surfaced
    /// as SQL, never fatal to the process.
    /// </summary>
    public static CallToolResult SqlErrorOrConnection(SqlException ex)
    {
        if (IsTransient(ex))
        {
            return ConnectionError($"{ex.Message} Retries exhausted.");
        }
        return SqlError(ex);
    }

    /// <summary>
    /// True when <paramref name="ex"/> carries a documented transient error number.
    /// After SqlRetryLogicOption exhausts retries (ADR-0004), our code sees the final
    /// SqlException — we classify it here to decide CONNECTION vs SQL.
    /// </summary>
    public static bool IsTransient(SqlException ex)
    {
        // Check every error in the collection — SqlException can carry multiple.
        foreach (SqlError err in ex.Errors)
        {
            if (TransientErrorNumbers.Contains(err.Number))
            {
                return true;
            }
        }
        return TransientErrorNumbers.Contains(ex.Number);
    }

    public static CallToolResult Internal(Exception ex)
    {
        // ADR-0010 INTERNAL shape: {"error":"INTERNAL","exception_type":"...","detail":"..."}
        // Never include stack traces — those go to logs per ADR-0011.
        object payload = new
        {
            error = "INTERNAL",
            exception_type = ex.GetType().Name,
            detail = ex.Message,
        };
        return Text(JsonSerializer.Serialize(payload, JsonOptions), isError: true);
    }

    /// <summary>
    /// ADR-0010 CONNECTION class. Used for cross-DB validation failures (database not found,
    /// not online, not multi-user) and transient connection errors after retries exhausted.
    /// </summary>
    public static CallToolResult ConnectionError(string detail)
    {
        object payload = new
        {
            error = "CONNECTION",
            detail = detail,
        };
        return Text(JsonSerializer.Serialize(payload, JsonOptions), isError: true);
    }

    /// <summary>
    /// ADR-0010 OBJECT_NOT_FOUND class. Returned by <c>get_object_details</c> when the
    /// lookup returns zero rows — object lookup is never ambiguous, so absence is an error.
    /// </summary>
    public static CallToolResult ObjectNotFoundError(string? database, string schema, string name, string? type)
    {
        object payload = new
        {
            error = "OBJECT_NOT_FOUND",
            schema,
            name,
            type = type ?? "UNKNOWN",
            database = database ?? string.Empty,
        };
        return Text(JsonSerializer.Serialize(payload, JsonOptions), isError: true);
    }
}
