using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Integration tests for explain_query against a real SQL Server.
/// Tagged Category=Integration — skipped in CI via --filter Category!=Integration.
/// Requires MSSQL_CONNECTION_STRING env var pointing at a real SQL Server.
///
/// Oracle watch-out-for #2 (ADR-0016): confirms that <c>SET SHOWPLAN_XML OFF</c> in the
/// finally block + <c>Connection Reset=true</c> (Microsoft.Data.SqlClient default) clears
/// the session-scoped SHOWPLAN_XML setting so subsequent <c>execute_sql</c> calls on the
/// same pooled connection return rows (not plan XML).
/// </summary>
[Trait("Category", "Integration")]
public class ExplainQueryIntegrationTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING");

    private static MssqlMcpOptions CreateOptions()
    {
        return new MssqlMcpOptions
        {
            ConnectionString = ConnectionString!,
            AccessMode = AccessMode.Restricted,
            QueryTimeout = 30,
            LogLevel = "info",
            MaxResultBytes = 10 * 1024 * 1024,
            RetryCount = 3,
            RetryIntervalMin = 2,
            RetryIntervalMax = 10,
        };
    }

    private static PlanTools CreatePlanTools(MssqlMcpOptions options)
    {
        ISqlExecutor executor = new SqlExecutor(options.ConnectionString, options.QueryTimeout,
            NullLogger<SqlExecutor>.Instance);
        IGuard guard = new SqlGuard(options, NullLogger<SqlGuard>.Instance);
        return new PlanTools(executor, guard, Options.Create(options), NullLogger<PlanTools>.Instance);
    }

    private static SqlTools CreateSqlTools(MssqlMcpOptions options)
    {
        ISqlExecutor executor = new SqlExecutor(options.ConnectionString, options.QueryTimeout,
            NullLogger<SqlExecutor>.Instance);
        IGuard guard = new SqlGuard(options, NullLogger<SqlGuard>.Instance);
        return new SqlTools(executor, guard, Options.Create(options), NullLogger<SqlTools>.Instance);
    }

    private static string GetText(CallToolResult result)
    {
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    [Fact(Skip = "Integration test — set MSSQL_CONNECTION_STRING and run without the Category!=Integration filter.")]
    public async Task ExplainQuery_RealDb_ReturnsPlanWithCost()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        MssqlMcpOptions options = CreateOptions();
        PlanTools tools = CreatePlanTools(options);
        CallToolResult result = await tools.ExplainQuery(
            "SELECT * FROM sys.objects",
            format: "summary",
            CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("estimated_total_cost", out JsonElement cost));
        Assert.True(cost.GetDouble() > 0, "Expected estimated_total_cost > 0 for a real plan.");
        Assert.True(doc.RootElement.TryGetProperty("top_operations", out JsonElement ops));
        Assert.Equal(JsonValueKind.Array, ops.ValueKind);
        Assert.True(ops.GetArrayLength() > 0, "Expected at least one RelOp in the plan.");
    }

    [Fact(Skip = "Integration test — set MSSQL_CONNECTION_STRING and run without the Category!=Integration filter.")]
    public async Task ExplainQuery_RealDb_XmlFormat_ReturnsRawXml()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        MssqlMcpOptions options = CreateOptions();
        PlanTools tools = CreatePlanTools(options);
        CallToolResult result = await tools.ExplainQuery(
            "SELECT * FROM sys.objects",
            format: "xml",
            CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string text = GetText(result);
        Assert.Contains("<ShowPlanXML", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Oracle watch-out-for #2 (ADR-0016): after <c>explain_query</c>, a subsequent
    /// <c>execute_sql</c> on the SAME connection pool MUST return rows (not plan XML).
    /// This verifies that <c>SET SHOWPLAN_XML OFF</c> in the finally block + the default
    /// <c>Connection Reset=true</c> clears the session-scoped SHOWPLAN_XML setting.
    /// </summary>
    [Fact(Skip = "Integration test — set MSSQL_CONNECTION_STRING and run without the Category!=Integration filter.")]
    public async Task ExplainQuery_RealDb_SubsequentExecuteSqlReturnsRows()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        MssqlMcpOptions options = CreateOptions();

        // Step 1: explain_query — this turns SHOWPLAN_XML ON then OFF on a pooled connection.
        PlanTools planTools = CreatePlanTools(options);
        CallToolResult planResult = await planTools.ExplainQuery(
            "SELECT TOP 1 name FROM sys.objects",
            format: "summary",
            CancellationToken.None);
        Assert.False(planResult.IsError ?? false);

        // Step 2: execute_sql on the same connection pool. If SHOWPLAN_XML leaked (Oracle
        // watch-out-for #2), this would return plan XML instead of rows, and JSON parsing
        // would fail (or the array would contain a single string, not row objects).
        SqlTools sqlTools = CreateSqlTools(options);
        CallToolResult sqlResult = await sqlTools.ExecuteSql(
            "SELECT TOP 1 name FROM sys.objects",
            CancellationToken.None);

        Assert.False(sqlResult.IsError ?? false, "execute_sql should succeed after explain_query.");
        string json = GetText(sqlResult);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() > 0, "Expected at least one row, not a plan XML string.");
        foreach (JsonElement row in doc.RootElement.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Object, row.ValueKind);
            Assert.True(row.TryGetProperty("name", out _));
        }
    }
}
