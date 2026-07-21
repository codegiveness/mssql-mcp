using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Unit tests for the explain_query tool (ADR-0016 explain_query schema, ADR-0010 error shapes).
/// Fakes ISqlExecutor + IGuard — no real DB. Canned SHOWPLAN_XML comes from <see cref="CannedShowPlanXml"/>.
/// </summary>
public class ExplainQueryTests
{
    private const int DefaultTimeoutMs = 30_000;

    private static MssqlMcpOptions RestrictedOptions() => new()
    {
        ConnectionString = "Server=localhost;",
        AccessMode = AccessMode.Restricted,
        QueryTimeout = 30,
        LogLevel = "info",
        MaxResultBytes = 10 * 1024 * 1024,
        RetryCount = 3,
        RetryIntervalMin = 2,
        RetryIntervalMax = 10,
    };

    private static MssqlMcpOptions UnrestrictedOptions() => new()
    {
        ConnectionString = "Server=localhost;",
        AccessMode = AccessMode.Unrestricted,
        QueryTimeout = 0,
        LogLevel = "info",
        MaxResultBytes = 10 * 1024 * 1024,
        RetryCount = 3,
        RetryIntervalMin = 2,
        RetryIntervalMax = 10,
    };

    private static PlanTools CreateTools(
        ISqlExecutor executor,
        MssqlMcpOptions options,
        IGuard? guard = null)
    {
        guard ??= new SqlGuard(options, NullLogger<SqlGuard>.Instance);
        return new PlanTools(executor, guard, Options.Create(options), NullLogger<PlanTools>.Instance);
    }

    private static string GetText(CallToolResult result)
    {
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    // ---------- Guard rejection (both modes) ----------

    [Fact]
    public async Task ExplainQuery_GuardRejectsDropTable_ReturnsGuardRejection()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExplainQuery("DROP TABLE nonexistent", format: null, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("GUARD_REJECTION", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("non_select_statement", doc.RootElement.GetProperty("rule").GetString());

        // Guard rejection must never reach SHOWPLAN execution.
        await executor.DidNotReceive().ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExplainQuery_GuardValidatesInBothModes()
    {
        // In Unrestricted mode, execute_sql SKIPS the Guard. explain_query MUST NOT — it
        // always calls ValidateStrict (ADR-0016). Use a fake IGuard to assert the call.
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CannedShowPlanXml.SimplePlan);

        IGuard guard = Substitute.For<IGuard>();
        guard.ValidateStrict(Arg.Any<string>())
            .Returns(GuardResult.Accept("/* mssql-mcp */\nBEGIN TRANSACTION\nSELECT TOP 5 * FROM sys.objects\nROLLBACK TRANSACTION"));

        PlanTools tools = CreateTools(executor, UnrestrictedOptions(), guard);
        await tools.ExplainQuery("SELECT TOP 5 * FROM sys.objects", format: "xml", CancellationToken.None);

        // ValidateStrict MUST be called even in Unrestricted mode.
        guard.Received(1).ValidateStrict(Arg.Any<string>());
        // Validate (the non-strict one) MUST NOT be called — explain_query uses ValidateStrict.
        guard.DidNotReceive().Validate(Arg.Any<string>());
    }

    [Fact]
    public async Task ExplainQuery_PassesWrappedSqlToExecutor()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CannedShowPlanXml.SimplePlan);

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        await tools.ExplainQuery("SELECT TOP 5 * FROM sys.objects", format: "xml", CancellationToken.None);

        // The wrapped SQL must contain sentinel + BEGIN TRAN / ROLLBACK (ADR-0007).
        await executor.Received(1).ExecuteShowPlanXmlAsync(
            Arg.Is<string>(s => s != null
                                && s.Contains("/* mssql-mcp */", StringComparison.Ordinal)
                                && s.Contains("BEGIN TRANSACTION", StringComparison.Ordinal)
                                && s.Contains("ROLLBACK TRANSACTION", StringComparison.Ordinal)
                                && s.Contains("SELECT TOP 5 * FROM sys.objects", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    // ---------- Summary format ----------

    [Fact]
    public async Task ExplainQuery_SummaryFormat_ExtractsCost()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CannedShowPlanXml.FullPlan);

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExplainQuery("SELECT * FROM dbo.Orders", format: "summary", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("estimated_total_cost", out JsonElement cost));
        Assert.Equal(0.0065, cost.GetDouble(), precision: 4);
    }

    [Fact]
    public async Task ExplainQuery_SummaryFormat_ExtractsMissingIndexes()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CannedShowPlanXml.FullPlan);

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExplainQuery("SELECT * FROM dbo.Orders", format: "summary", CancellationToken.None);

        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement indexes = doc.RootElement.GetProperty("missing_indexes");
        Assert.Equal(JsonValueKind.Array, indexes.ValueKind);
        Assert.Equal(1, indexes.GetArrayLength());

        JsonElement idx = indexes[0];
        Assert.Equal(95.0, idx.GetProperty("impact").GetDouble(), precision: 1);
        Assert.Equal("AppDb", idx.GetProperty("database").GetString());
        Assert.Equal("dbo", idx.GetProperty("schema").GetString());
        Assert.Equal("Orders", idx.GetProperty("table").GetString());
        Assert.True(idx.TryGetProperty("equality_columns", out JsonElement eqCols));
        Assert.Contains("[Status]", eqCols.GetString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainQuery_SummaryFormat_ExtractsWarnings()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CannedShowPlanXml.FullPlan);

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExplainQuery("SELECT * FROM dbo.Orders", format: "summary", CancellationToken.None);

        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement warnings = doc.RootElement.GetProperty("warnings");
        Assert.Equal(JsonValueKind.Array, warnings.ValueKind);
        Assert.Equal(1, warnings.GetArrayLength());
        Assert.Equal("NO_JOIN_PREDICATE", warnings[0].GetString());
    }

    [Fact]
    public async Task ExplainQuery_SummaryFormat_ExtractsTopOperations()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CannedShowPlanXml.PlanWithSixRelOps);

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExplainQuery("SELECT 1", format: "summary", CancellationToken.None);

        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement ops = doc.RootElement.GetProperty("top_operations");
        Assert.Equal(JsonValueKind.Array, ops.ValueKind);
        // Top 5 only — six RelOps in the canned plan must truncate to 5.
        Assert.Equal(5, ops.GetArrayLength());

        // Sorted descending by estimated_cost (Index Scan with 0.0055 first, Filter with 0.00001 last).
        double[] costs = ops.EnumerateArray()
            .Select(o => o.GetProperty("estimated_cost").GetDouble())
            .ToArray();
        for (int i = 1; i < costs.Length; i++)
        {
            Assert.True(costs[i - 1] >= costs[i],
                $"top_operations not sorted descending: [{string.Join(", ", costs)}] at index {i}");
        }

        // First operation must have operation name + object.
        JsonElement first = ops[0];
        Assert.Equal("Index Scan", first.GetProperty("operation").GetString());
        Assert.True(first.TryGetProperty("estimated_rows", out _));
        Assert.True(first.TryGetProperty("object", out JsonElement obj));
        Assert.Contains("[AppDb]", obj.GetString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainQuery_SummaryFormat_NoMissingIndexes_ReturnsEmptyArray()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CannedShowPlanXml.SimplePlan);

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExplainQuery("SELECT 1", format: "summary", CancellationToken.None);

        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement indexes = doc.RootElement.GetProperty("missing_indexes");
        Assert.Equal(JsonValueKind.Array, indexes.ValueKind);
        Assert.Equal(0, indexes.GetArrayLength());
        JsonElement warnings = doc.RootElement.GetProperty("warnings");
        Assert.Equal(0, warnings.GetArrayLength());
    }

    // ---------- XML format ----------

    [Fact]
    public async Task ExplainQuery_XmlFormat_ReturnsRawXml()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CannedShowPlanXml.SimplePlan);

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExplainQuery("SELECT 1", format: "xml", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string text = GetText(result);

        // Raw XML string returned as-is (NOT wrapped in a JSON envelope).
        Assert.StartsWith("<?xml", text, StringComparison.Ordinal);
        Assert.Contains("<ShowPlanXML", text, StringComparison.Ordinal);
    }

    // ---------- Default format ----------

    [Fact]
    public async Task ExplainQuery_DefaultFormat_IsSummary()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CannedShowPlanXml.SimplePlan);

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExplainQuery("SELECT 1", format: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        // Summary shape: object with these keys (not a raw XML string).
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("estimated_total_cost", out _));
        Assert.True(doc.RootElement.TryGetProperty("missing_indexes", out _));
        Assert.True(doc.RootElement.TryGetProperty("warnings", out _));
        Assert.True(doc.RootElement.TryGetProperty("top_operations", out _));
    }

    [Fact]
    public async Task ExplainQuery_UnknownFormatFallsBackToSummary()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CannedShowPlanXml.SimplePlan);

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExplainQuery("SELECT 1", format: "summry", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("estimated_total_cost", out _));
    }

    // ---------- Error paths ----------

    [Fact]
    public async Task ExplainQuery_SqlException_ReturnsSqlError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: 208, message: "Invalid object name 'Orders'.", severity: 16, line: 1));

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExplainQuery("SELECT * FROM Orders", format: null, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("SQL", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("SQL208", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("Invalid object name 'Orders'.", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal(16, doc.RootElement.GetProperty("severity").GetInt32());
    }

    [Fact]
    public async Task ExplainQuery_Timeout_ReturnsTimeoutError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExplainQuery("SELECT 1", format: null, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("TIMEOUT", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(DefaultTimeoutMs, doc.RootElement.GetProperty("timeout_ms").GetInt32());
    }

    [Fact]
    public async Task ExplainQuery_ClientCancellation_Rethrows_NotTimeout()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        using CancellationTokenSource cts = new();
        cts.Cancel();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException(cts.Token));

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await tools.ExplainQuery("SELECT 1", format: null, cts.Token));
    }

    [Fact]
    public async Task ExplainQuery_UnexpectedException_ReturnsInternalError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("boom"));

        PlanTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExplainQuery("SELECT 1", format: null, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("INTERNAL", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("InvalidOperationException", doc.RootElement.GetProperty("exception_type").GetString());
    }

    [Fact]
    public async Task ExplainQuery_EmptyInput_ReturnsGuardRejection()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        PlanTools tools = CreateTools(executor, RestrictedOptions());

        CallToolResult result = await tools.ExplainQuery("   ", format: null, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("GUARD_REJECTION", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("empty_batch", doc.RootElement.GetProperty("rule").GetString());
        await executor.DidNotReceive().ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
