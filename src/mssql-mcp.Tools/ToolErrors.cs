using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core.Guard;
using mssql_mcp.Core.Logging;

namespace mssql_mcp.Tools;

/// <summary>
/// Shared error-response helpers for all MCP tools. Produces <see cref="CallToolResult"/>
/// with structured JSON per ADR-0010. Used by <see cref="SqlTools"/>, <see cref="DatabaseTools"/>,
/// and future tool classes to avoid drift.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Anonymous payload types are compiler-generated with fully-known structure. Properties are preserved by the compiler and discovered via reflection at runtime.")]
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

    /// <summary>
    /// Serializes <paramref name="items"/> as a JSON array, stopping when the accumulated UTF-8
    /// byte count crosses <paramref name="maxBytes"/> (ADR-0003 transport safety net). When
    /// truncated, returns TWO <see cref="TextContentBlock"/> items: the JSON array first
    /// (closed at the point of truncation), then a truncation notice. <paramref name="maxBytes"/>
    /// of <c>0</c> disables the cap and serializes all items. Accepts a covariant list so
    /// both <see cref="List{Dictionary{String,Object}}"/> (rowsets) and <see cref="List{Object}"/>
    /// (mixed payloads like <c>list_objects</c>) work without copy or overload selection.
    /// </summary>
    /// <param name="items">Payload elements per ADR-0009 (rows or status objects).</param>
    /// <param name="maxBytes">Byte threshold. <c>0</c> disables (no truncation).</param>
    /// <param name="logger">Optional logger for the truncation event.</param>
    /// <returns>
    /// A <see cref="CallToolResult"/> with one <see cref="TextContentBlock"/> (no truncation)
    /// or two (data + notice). The data block is always a valid JSON array.
    /// </returns>
    public static CallToolResult SuccessWithByteCap(
        IReadOnlyList<object> items,
        long maxBytes,
        Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (maxBytes <= 0)
        {
            string allJson = JsonSerializer.Serialize(items, JsonOptions);
            return Success(allJson);
        }

        // Build the array incrementally so we can stop at the threshold without re-serializing.
        // Each item is a complete JSON value (object, string, number) — keep it whole so the
        // outer array is valid JSON. Prefix with comma for all but the first element.
        StringBuilder buffer = new();
        buffer.Append('[');

        long totalBytes = 1;
        int returned = 0;
        bool truncated = false;

        for (int i = 0; i < items.Count; i++)
        {
            string itemJson = JsonSerializer.Serialize(items[i], JsonOptions);
            string segment = (i == 0 ? string.Empty : ",") + itemJson;

            int segmentBytes = Encoding.UTF8.GetByteCount(segment);
            if (totalBytes + segmentBytes > maxBytes)
            {
                truncated = true;
                break;
            }

            buffer.Append(segment);
            totalBytes += segmentBytes;
            returned++;
        }

        buffer.Append(']');

        if (!truncated)
        {
            return Success(buffer.ToString());
        }

        string notice = $"[truncated] Result exceeded {maxBytes} bytes. {returned} rows returned, more exist. Narrow with WHERE, TOP, or OFFSET/FETCH.";
        logger?.LogWarning("[byte-cap] Truncated at {Bytes} bytes, {Rows} rows returned (threshold {Threshold})", totalBytes, returned, maxBytes);

        // Two TextContent items: data array first, truncation notice second.
        return new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = buffer.ToString() },
                new TextContentBlock { Text = notice },
            },
            IsError = false,
        };
    }

    /// <summary>
    /// Truncates <paramref name="text"/> when its UTF-8 byte count crosses
    /// <paramref name="maxBytes"/> (ADR-0003 transport safety net for non-array payloads like
    /// <c>explain_query</c>'s raw SHOWPLAN_XML). <paramref name="maxBytes"/> of <c>0</c> disables.
    /// When truncated, returns TWO <see cref="TextContentBlock"/> items: the text (cut to fit
    /// under the byte threshold) first, then a truncation notice as the second item.
    /// </summary>
    public static CallToolResult SuccessWithByteCap(
        string text,
        long maxBytes,
        Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (maxBytes <= 0)
        {
            return Success(text);
        }

        int textBytes = Encoding.UTF8.GetByteCount(text);
        if (textBytes <= maxBytes)
        {
            return Success(text);
        }

        // Truncate at a character boundary so the result is valid UTF-8 (and valid XML when the
        // input is XML). Binary search for the largest char count whose UTF-8 encoding fits under
        // maxBytes — O(log n) instead of decrementing one char at a time.
        int lo = 0;
        int hi = text.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            if (Encoding.UTF8.GetByteCount(text.AsSpan(0, mid)) <= maxBytes)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
        int charCount = lo;
        string truncatedText = text.Substring(0, charCount);

        string notice = $"[truncated] Result exceeded {maxBytes} bytes. Truncated to {charCount} characters. Narrow the query or request summary format.";
        logger?.LogWarning("[byte-cap] Truncated text from {Original} to {Truncated} chars (threshold {Threshold} bytes)", text.Length, charCount, maxBytes);

        return new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = truncatedText },
                new TextContentBlock { Text = notice },
            },
            IsError = false,
        };
    }

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
            message = PasswordObfuscator.Obfuscate(first?.Message ?? ex.Message),
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
        // ex.Number delegates to ex.Errors[0].Number, so the loop covers the populated case.
        // When ex.Errors is empty, ex.Number returns 0 (non-transient) — correct.
        foreach (SqlError err in ex.Errors)
        {
            if (TransientErrorNumbers.Contains(err.Number))
            {
                return true;
            }
        }
        return false;
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
