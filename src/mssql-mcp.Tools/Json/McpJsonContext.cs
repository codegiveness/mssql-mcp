using System.Text.Json;
using System.Text.Json.Serialization;

namespace mssql_mcp.Tools.Json;

/// <summary>
/// Source-generated JSON serializer context for the mssql-mcp tools layer.
/// Mirrors <see cref="mssql_mcp.Tools.ToolErrors.JsonOptions"/>:
/// <see cref="System.Text.Json.JsonSerializerDefaults.Web"/> (camelCase, case-insensitive)
/// with <c>WriteIndented = false</c>.
/// </summary>
/// <remarks>
/// This context is the expand-phase foundation (ticket #47): DTO records and primitive types
/// are registered here so that a later contract phase can swap reflection-based serialization
/// for source-generated serialization without changing call sites. No production code references
/// this context yet.
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = false)]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(IReadOnlyList<object>))]
[JsonSerializable(typeof(List<object>))]
[JsonSerializable(typeof(List<Dictionary<string, object?>>))]
[JsonSerializable(typeof(GuardRejectionPayload))]
[JsonSerializable(typeof(GuardRejectionPayload.PositionDto))]
[JsonSerializable(typeof(TimeoutPayload))]
[JsonSerializable(typeof(SqlErrorPayload))]
[JsonSerializable(typeof(InternalErrorPayload))]
[JsonSerializable(typeof(ConnectionErrorPayload))]
[JsonSerializable(typeof(ObjectNotFoundPayload))]
[JsonSerializable(typeof(QueryPlanSummary))]
[JsonSerializable(typeof(QueryPlanOperation))]
[JsonSerializable(typeof(MissingIndexPayload))]
[JsonSerializable(typeof(RowLimitNotice))]
[JsonSerializable(typeof(TruncationNotice))]
[JsonSerializable(typeof(DmlStatusPayload))]
[JsonSerializable(typeof(DdlStatusPayload))]
[JsonSerializable(typeof(DbHealthSizeSummary))]
[JsonSerializable(typeof(DbHealthVlfSummary))]
[JsonSerializable(typeof(DbHealthFragmentationSummary))]
[JsonSerializable(typeof(DbHealthStatsSummary))]
[JsonSerializable(typeof(DbHealthBlockingSummary))]
internal sealed partial class McpJsonContext : JsonSerializerContext
{
}
