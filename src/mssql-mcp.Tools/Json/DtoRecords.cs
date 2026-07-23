using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace mssql_mcp.Tools.Json;

/// <summary>
/// DTO records for every anonymous-type payload in the serialization/error layer.
/// Each record carries <c>[JsonPropertyName]</c> on every property so that source-generated
/// serialization produces byte-for-byte identical JSON to the existing reflection-based path
/// (which uses <see cref="System.Text.Json.JsonSerializerDefaults.Web"/> with snake_case
/// property names in the anonymous types).
/// </summary>

// ---------- ADR-0010 error payloads (ToolErrors.cs) ----------

/// <summary>
/// GUARD_REJECTION error payload (ADR-0010). Position is null when both Line and Column
/// are null; otherwise carries the 1-based token position.
/// </summary>
public sealed record GuardRejectionPayload
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("rule")]
    public string Rule { get; init; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;

    [JsonPropertyName("statement_type")]
    public string StatementType { get; init; } = string.Empty;

    [JsonPropertyName("position")]
    public PositionDto? Position { get; init; }

    /// <summary>Nested position object: null when both line and column are null.</summary>
    public sealed record PositionDto
    {
        [JsonPropertyName("line")]
        public int? Line { get; init; }

        [JsonPropertyName("column")]
        public int? Column { get; init; }
    }
}

/// <summary>TIMEOUT error payload (ADR-0010).</summary>
public sealed record TimeoutPayload
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("timeout_ms")]
    public int TimeoutMs { get; init; }

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;
}

/// <summary>SQL error payload (ADR-0010). Severity is a T-SQL severity class (byte).</summary>
public sealed record SqlErrorPayload
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public byte Severity { get; init; }

    [JsonPropertyName("line")]
    public int Line { get; init; }

    [JsonPropertyName("procedure")]
    public string? Procedure { get; init; }
}

/// <summary>INTERNAL error payload (ADR-0010). Never includes stack traces.</summary>
public sealed record InternalErrorPayload
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("exception_type")]
    public string ExceptionType { get; init; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;
}

/// <summary>CONNECTION error payload (ADR-0010).</summary>
public sealed record ConnectionErrorPayload
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;
}

/// <summary>OBJECT_NOT_FOUND error payload (ADR-0010).</summary>
public sealed record ObjectNotFoundPayload
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("schema")]
    public string Schema { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("database")]
    public string Database { get; init; } = string.Empty;
}

// ---------- Query plan payloads (PlanTools.cs) ----------

/// <summary>
/// explain_query summary payload (ADR-0016). Carries estimated total cost, missing indexes,
/// warnings, and the top 5 operations by estimated cost.
/// </summary>
public sealed record QueryPlanSummary
{
    [JsonPropertyName("estimated_total_cost")]
    public double EstimatedTotalCost { get; init; }

    [JsonPropertyName("missing_indexes")]
    public List<MissingIndexPayload> MissingIndexes { get; init; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; init; } = new();

    [JsonPropertyName("top_operations")]
    public List<QueryPlanOperation> TopOperations { get; init; } = new();
}

/// <summary>Single top operation in a query plan summary.</summary>
public sealed record QueryPlanOperation
{
    [JsonPropertyName("operation")]
    public string Operation { get; init; } = string.Empty;

    [JsonPropertyName("estimated_cost")]
    public double EstimatedCost { get; init; }

    [JsonPropertyName("estimated_rows")]
    public double EstimatedRows { get; init; }

    [JsonPropertyName("object")]
    [SuppressMessage("Naming", "CA1720:IdentifiersShouldNotMatchTypeNames", Justification = "Matches the anonymous-type property name in PlanTools.BuildSummary (@object) for wire compatibility.")]
    public string? Object { get; init; }
}

/// <summary>Missing index recommendation extracted from SHOWPLAN_XML.</summary>
public sealed record MissingIndexPayload
{
    [JsonPropertyName("impact")]
    public double Impact { get; init; }

    [JsonPropertyName("database")]
    public string Database { get; init; } = string.Empty;

    [JsonPropertyName("schema")]
    public string Schema { get; init; } = string.Empty;

    [JsonPropertyName("table")]
    public string Table { get; init; } = string.Empty;

    [JsonPropertyName("equality_columns")]
    public string? EqualityColumns { get; init; }

    [JsonPropertyName("inequality_columns")]
    public string? InequalityColumns { get; init; }

    [JsonPropertyName("included_columns")]
    public string? IncludedColumns { get; init; }
}

// ---------- Discovery notice payloads ----------

/// <summary>Row-limit notice appended when a discovery result hits its row cap.</summary>
public sealed record RowLimitNotice
{
    [JsonPropertyName("notice")]
    public string Notice { get; init; } = string.Empty;

    [JsonPropertyName("limit")]
    public int Limit { get; init; }
}

/// <summary>Truncation notice prepended to a list_objects payload when the row cap is hit.</summary>
public sealed record TruncationNotice
{
    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }

    [JsonPropertyName("returned")]
    public int Returned { get; init; }

    [JsonPropertyName("note")]
    public string Note { get; init; } = string.Empty;
}
