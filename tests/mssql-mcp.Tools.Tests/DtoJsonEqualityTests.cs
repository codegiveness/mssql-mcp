using System.Text.Json;
using mssql_mcp.Tools.Json;
using mssql_mcp.Core.Guard;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Byte-for-byte JSON equality tests proving each new DTO record serializes identically
/// to its anonymous-type counterpart under <see cref="ToolErrors.JsonOptions"/>
/// (JsonSerializerDefaults.Web + WriteIndented=false). This is the expand-phase contract
/// for ticket #47: the new source-gen-ready DTOs must be wire-compatible with the existing
/// reflection-based path so a future contract phase can swap them without behavior change.
/// </summary>
public class DtoJsonEqualityTests
{
    // Mirror the production options exactly. McpJsonContext.Default.Options is source-generated
    // from the same JsonSerializerDefaults.Web + WriteIndented=false.
    private static readonly JsonSerializerOptions DtoOptions = McpJsonContext.Default.Options;
    private static readonly JsonSerializerOptions AnonymousOptions = ToolErrors.JsonOptions;

    // ---------- GuardRejectionPayload ----------

    [Fact]
    public void GuardRejection_WithPosition_SerializesIdenticallyToAnonymous()
    {
        GuardRejection rejection = new(
            rule: "select_into",
            detail: "[guard] Restricted mode: SELECT ... INTO is not permitted.",
            statementType: "SELECT_INTO",
            line: 42,
            column: 7);

        GuardRejectionPayload dto = new()
        {
            Error = "GUARD_REJECTION",
            Rule = rejection.Rule,
            Detail = rejection.Detail,
            StatementType = rejection.StatementType ?? string.Empty,
            Position = new GuardRejectionPayload.PositionDto { Line = rejection.Line, Column = rejection.Column },
        };

        object anonymous = new
        {
            error = "GUARD_REJECTION",
            rule = rejection.Rule,
            detail = rejection.Detail,
            statement_type = rejection.StatementType ?? string.Empty,
            position = new { line = rejection.Line, column = rejection.Column },
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    [Fact]
    public void GuardRejection_WithoutPosition_SerializesIdenticallyToAnonymous()
    {
        // Both Line and Column null → position is null in both paths.
        GuardRejection rejection = new(
            rule: "parse_error",
            detail: "[guard] Restricted mode: SQL could not be parsed.",
            statementType: null);

        GuardRejectionPayload dto = new()
        {
            Error = "GUARD_REJECTION",
            Rule = rejection.Rule,
            Detail = rejection.Detail,
            StatementType = rejection.StatementType ?? string.Empty,
            Position = null,
        };

        object anonymous = new
        {
            error = "GUARD_REJECTION",
            rule = rejection.Rule,
            detail = rejection.Detail,
            statement_type = rejection.StatementType ?? string.Empty,
            position = (int?)null != null ? new { line = (int?)null, column = (int?)null } : null,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    // ---------- TimeoutPayload ----------

    [Fact]
    public void Timeout_SerializesIdenticallyToAnonymous()
    {
        int timeoutSeconds = 30;
        int timeoutMs = timeoutSeconds * 1000;

        TimeoutPayload dto = new()
        {
            Error = "TIMEOUT",
            TimeoutMs = timeoutMs,
            Detail = $"Query exceeded {timeoutSeconds}s command timeout",
        };

        object anonymous = new
        {
            error = "TIMEOUT",
            timeout_ms = timeoutMs,
            detail = $"Query exceeded {timeoutSeconds}s command timeout",
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    [Fact]
    public void Timeout_ZeroSeconds_SerializesIdenticallyToAnonymous()
    {
        int timeoutSeconds = 0;

        TimeoutPayload dto = new()
        {
            Error = "TIMEOUT",
            TimeoutMs = 0,
            Detail = $"Query exceeded {timeoutSeconds}s command timeout",
        };

        object anonymous = new
        {
            error = "TIMEOUT",
            timeout_ms = 0,
            detail = $"Query exceeded {timeoutSeconds}s command timeout",
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    // ---------- SqlErrorPayload ----------

    [Fact]
    public void SqlError_WithProcedure_SerializesIdenticallyToAnonymous()
    {
        byte severity = 14;
        int line = 12;

        SqlErrorPayload dto = new()
        {
            Error = "SQL",
            Code = "SQL208",
            Message = "Obfuscated error message",
            Severity = severity,
            Line = line,
            Procedure = "usp_DoThing",
        };

        object anonymous = new
        {
            error = "SQL",
            code = "SQL208",
            message = "Obfuscated error message",
            severity = severity,
            line = line,
            procedure = "usp_DoThing",
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    [Fact]
    public void SqlError_NullProcedure_SerializesIdenticallyToAnonymous()
    {
        byte severity = 0;
        int line = 0;

        SqlErrorPayload dto = new()
        {
            Error = "SQL",
            Code = "SQL0",
            Message = string.Empty,
            Severity = severity,
            Line = line,
            Procedure = null,
        };

        object anonymous = new
        {
            error = "SQL",
            code = "SQL0",
            message = string.Empty,
            severity = severity,
            line = line,
            procedure = (string?)null,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    [Fact]
    public void SqlError_MaxSeverity_SerializesIdenticallyToAnonymous()
    {
        byte severity = 25;

        SqlErrorPayload dto = new()
        {
            Error = "SQL",
            Code = "SQL500",
            Message = "Fatal",
            Severity = severity,
            Line = -1,
            Procedure = null,
        };

        object anonymous = new
        {
            error = "SQL",
            code = "SQL500",
            message = "Fatal",
            severity = severity,
            line = -1,
            procedure = (string?)null,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    // ---------- InternalErrorPayload ----------

    [Fact]
    public void Internal_SerializesIdenticallyToAnonymous()
    {
        var ex = new InvalidOperationException("Something went wrong");

        InternalErrorPayload dto = new()
        {
            Error = "INTERNAL",
            ExceptionType = ex.GetType().Name,
            Detail = ex.Message,
        };

        object anonymous = new
        {
            error = "INTERNAL",
            exception_type = ex.GetType().Name,
            detail = ex.Message,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    // ---------- ConnectionErrorPayload ----------

    [Fact]
    public void ConnectionError_SerializesIdenticallyToAnonymous()
    {
        string detail = "Database 'ghostdb' not found. Retries exhausted.";

        ConnectionErrorPayload dto = new()
        {
            Error = "CONNECTION",
            Detail = detail,
        };

        object anonymous = new
        {
            error = "CONNECTION",
            detail = detail,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    [Fact]
    public void ConnectionError_EmptyDetail_SerializesIdenticallyToAnonymous()
    {
        ConnectionErrorPayload dto = new()
        {
            Error = "CONNECTION",
            Detail = string.Empty,
        };

        object anonymous = new
        {
            error = "CONNECTION",
            detail = string.Empty,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    // ---------- ObjectNotFoundPayload ----------

    [Fact]
    public void ObjectNotFound_WithDatabaseAndType_SerializesIdenticallyToAnonymous()
    {
        string? database = "AppDb";
        string schema = "dbo";
        string name = "Orders";
        string? type = "TABLE";

        ObjectNotFoundPayload dto = new()
        {
            Error = "OBJECT_NOT_FOUND",
            Schema = schema,
            Name = name,
            Type = type ?? "UNKNOWN",
            Database = database ?? string.Empty,
        };

        object anonymous = new
        {
            error = "OBJECT_NOT_FOUND",
            schema,
            name,
            type = type ?? "UNKNOWN",
            database = database ?? string.Empty,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    [Fact]
    public void ObjectNotFound_NullDatabaseAndType_SerializesIdenticallyToAnonymous()
    {
        string? database = null;
        string schema = "dbo";
        string name = "MissingView";
        string? type = null;

        ObjectNotFoundPayload dto = new()
        {
            Error = "OBJECT_NOT_FOUND",
            Schema = schema,
            Name = name,
            Type = type ?? "UNKNOWN",
            Database = database ?? string.Empty,
        };

        object anonymous = new
        {
            error = "OBJECT_NOT_FOUND",
            schema,
            name,
            type = type ?? "UNKNOWN",
            database = database ?? string.Empty,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    // ---------- QueryPlanSummary (top-level summary) ----------

    [Fact]
    public void QueryPlanSummary_FullPayload_SerializesIdenticallyToAnonymous()
    {
        double totalCost = 1.2345;

        List<MissingIndexPayload> missingIndexes = new()
        {
            new MissingIndexPayload
            {
                Impact = 95.0,
                Database = "AppDb",
                Schema = "dbo",
                Table = "Orders",
                EqualityColumns = "[OrderId]",
                InequalityColumns = null,
                IncludedColumns = "[CustomerName]",
            },
        };

        List<string> warnings = new() { "NO_JOIN_PREDICATE", "MISSING_JOIN_PREDICATE" };

        List<QueryPlanOperation> topOps = new()
        {
            new QueryPlanOperation
            {
                Operation = "Index Seek",
                EstimatedCost = 0.003283,
                EstimatedRows = 100,
                Object = "AppDb.dbo.Orders",
            },
            new QueryPlanOperation
            {
                Operation = "Clustered Index Scan",
                EstimatedCost = 0.5,
                EstimatedRows = 0,
                Object = null,
            },
        };

        QueryPlanSummary dto = new()
        {
            EstimatedTotalCost = Math.Round(totalCost, 4, MidpointRounding.ToEven),
            MissingIndexes = missingIndexes,
            Warnings = warnings,
            TopOperations = topOps,
        };

        // Anonymous counterpart mirrors PlanTools.BuildSummary exactly.
        object anonymous = new
        {
            estimated_total_cost = Math.Round(totalCost, 4, MidpointRounding.ToEven),
            missing_indexes = missingIndexes.Select(static mi => (object)new
            {
                impact = mi.Impact,
                database = mi.Database,
                schema = mi.Schema,
                table = mi.Table,
                equality_columns = mi.EqualityColumns,
                inequality_columns = mi.InequalityColumns,
                included_columns = mi.IncludedColumns,
            }).ToList(),
            warnings,
            top_operations = topOps.Select(static r => (object)new
            {
                operation = r.Operation,
                estimated_cost = r.EstimatedCost,
                estimated_rows = r.EstimatedRows,
                @object = r.Object,
            }).ToList(),
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    [Fact]
    public void QueryPlanSummary_EmptyLists_SerializesIdenticallyToAnonymous()
    {
        QueryPlanSummary dto = new()
        {
            EstimatedTotalCost = 0.0,
            MissingIndexes = new List<MissingIndexPayload>(),
            Warnings = new List<string>(),
            TopOperations = new List<QueryPlanOperation>(),
        };

        object anonymous = new
        {
            estimated_total_cost = 0.0,
            missing_indexes = new List<object>(),
            warnings = new List<string>(),
            top_operations = new List<object>(),
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    // ---------- MissingIndexPayload ----------

    [Fact]
    public void MissingIndex_AllColumnGroups_SerializesIdenticallyToAnonymous()
    {
        MissingIndexPayload dto = new()
        {
            Impact = 87.5,
            Database = "AppDb",
            Schema = "sales",
            Table = "Orders",
            EqualityColumns = "[CustomerId], [OrderDate]",
            InequalityColumns = "[TotalAmount]",
            IncludedColumns = "[Notes]",
        };

        object anonymous = new
        {
            impact = 87.5,
            database = "AppDb",
            schema = "sales",
            table = "Orders",
            equality_columns = "[CustomerId], [OrderDate]",
            inequality_columns = "[TotalAmount]",
            included_columns = "[Notes]",
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    [Fact]
    public void MissingIndex_NullColumnGroups_SerializesIdenticallyToAnonymous()
    {
        // Mirrors the empty ColumnGroup case: all column-group strings are null.
        MissingIndexPayload dto = new()
        {
            Impact = 0.0,
            Database = string.Empty,
            Schema = string.Empty,
            Table = string.Empty,
            EqualityColumns = null,
            InequalityColumns = null,
            IncludedColumns = null,
        };

        object anonymous = new
        {
            impact = 0.0,
            database = string.Empty,
            schema = string.Empty,
            table = string.Empty,
            equality_columns = (string?)null,
            inequality_columns = (string?)null,
            included_columns = (string?)null,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    // ---------- QueryPlanOperation ----------

    [Fact]
    public void QueryPlanOperation_WithObject_SerializesIdenticallyToAnonymous()
    {
        QueryPlanOperation dto = new()
        {
            Operation = "Index Seek",
            EstimatedCost = 0.003283,
            EstimatedRows = 42,
            Object = "AppDb.dbo.Orders.IX_Orders_CustomerId",
        };

        object anonymous = new
        {
            operation = "Index Seek",
            estimated_cost = 0.003283,
            estimated_rows = 42d,
            @object = "AppDb.dbo.Orders.IX_Orders_CustomerId",
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    [Fact]
    public void QueryPlanOperation_NullObject_SerializesIdenticallyToAnonymous()
    {
        QueryPlanOperation dto = new()
        {
            Operation = string.Empty,
            EstimatedCost = 0.0,
            EstimatedRows = 0.0,
            Object = null,
        };

        object anonymous = new
        {
            operation = string.Empty,
            estimated_cost = 0.0,
            estimated_rows = 0.0,
            @object = (string?)null,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    // ---------- RowLimitNotice ----------

    [Fact]
    public void RowLimitNotice_SerializesIdenticallyToAnonymous()
    {
        RowLimitNotice dto = new()
        {
            Notice = "row limit hit",
            Limit = 1000,
        };

        object anonymous = new
        {
            notice = "row limit hit",
            limit = 1000,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    [Fact]
    public void RowLimitNotice_ZeroLimit_SerializesIdenticallyToAnonymous()
    {
        RowLimitNotice dto = new()
        {
            Notice = string.Empty,
            Limit = 0,
        };

        object anonymous = new
        {
            notice = string.Empty,
            limit = 0,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    // ---------- TruncationNotice (list_objects truncation payload) ----------

    [Fact]
    public void TruncationNotice_SerializesIdenticallyToAnonymous()
    {
        int clampedLimit = 1000;

        TruncationNotice dto = new()
        {
            Truncated = true,
            Returned = clampedLimit,
            Note = "Results truncated. Refine schema/type filters or raise limit.",
        };

        object anonymous = new
        {
            truncated = true,
            returned = clampedLimit,
            note = "Results truncated. Refine schema/type filters or raise limit.",
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    [Fact]
    public void TruncationNotice_NotTruncated_SerializesIdenticallyToAnonymous()
    {
        TruncationNotice dto = new()
        {
            Truncated = false,
            Returned = 0,
            Note = string.Empty,
        };

        object anonymous = new
        {
            truncated = false,
            returned = 0,
            note = string.Empty,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    // ---------- Dictionary row serialization ----------

    [Fact]
    public void DictionaryRow_MixedTypes_SerializesIdenticallyToAnonymousOptions()
    {
        // Mirrors the row shape returned by ExecuteQueryAsync: heterogeneous values keyed by column name.
        Dictionary<string, object?> row = new()
        {
            ["name"] = "Orders",
            ["database_id"] = 7,
            ["is_current"] = true,
            ["schema"] = null,
            ["size_mb"] = 128L,
            ["avg_fragmentation"] = 42.5,
        };

        string dtoJson = JsonSerializer.Serialize(row, DtoOptions);
        string anonymousJson = JsonSerializer.Serialize(row, AnonymousOptions);

        Assert.Equal(anonymousJson, dtoJson);
    }

    [Fact]
    public void DictionaryRow_Empty_SerializesIdenticallyToAnonymousOptions()
    {
        Dictionary<string, object?> row = new();

        string dtoJson = JsonSerializer.Serialize(row, DtoOptions);
        string anonymousJson = JsonSerializer.Serialize(row, AnonymousOptions);

        Assert.Equal(anonymousJson, dtoJson);
    }

    // ---------- DmlStatusPayload / DdlStatusPayload (ticket #49) ----------

    [Fact]
    public void DmlStatus_SerializesIdenticallyToAnonymous()
    {
        DmlStatusPayload dto = new()
        {
            Result = "success",
            StatementType = "INSERT",
            RowsAffected = 5,
        };

        object anonymous = new
        {
            result = "success",
            statement_type = "INSERT",
            rows_affected = 5,
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }

    [Fact]
    public void DdlStatus_SerializesIdenticallyToAnonymous()
    {
        DdlStatusPayload dto = new()
        {
            Result = "success",
            StatementType = "CREATE_TABLE",
            Object = "Orders",
        };

        object anonymous = new
        {
            result = "success",
            statement_type = "CREATE_TABLE",
            @object = "Orders",
        };

        Assert.Equal(JsonSerializer.Serialize(anonymous, AnonymousOptions), JsonSerializer.Serialize(dto, DtoOptions));
    }
}
