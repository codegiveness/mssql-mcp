using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Protocol;

namespace mssql_mcp.Tools;

/// <summary>
/// Shared error-response helpers for all MCP tools. Produces <see cref="CallToolResult"/>
/// with structured JSON per ADR-0010. Used by <see cref="SqlTools"/>, <see cref="DatabaseTools"/>,
/// and future tool classes to avoid drift.
/// </summary>
internal static class ToolErrors
{
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
}
