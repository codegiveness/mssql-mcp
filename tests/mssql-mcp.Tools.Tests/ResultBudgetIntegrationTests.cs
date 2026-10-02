using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;
using mssql_mcp.Tests;

namespace mssql_mcp.Tools.Tests;

[Trait("Category", "Integration")]
public class ResultBudgetIntegrationTests
{
    private const string ManyRows = "FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) a(n) CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) b(n)";

    private static SqlExecutor CreateExecutor(out MssqlMcpOptions options)
    {
        SqlConnectionStringBuilder connection = new(Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING"))
        {
            ApplicationName = "mssql-mcp-budget-" + Guid.NewGuid().ToString("N"),
            MaxPoolSize = 1,
        };
        options = new MssqlMcpOptions { ConnectionString = connection.ConnectionString, QueryTimeout = 30, RetryCount = 0 };
        return new SqlExecutor(options.ConnectionString, 30, 0, 2, 10, NullLogger<SqlExecutor>.Instance);
    }

    private static async Task AssertUsable(SqlExecutor executor)
    {
        SqlQueryResult next = await executor.ExecuteQueryAsync("SELECT 42 AS answer", 0, TestContext.Current.CancellationToken);
        Assert.False(next.IsTruncated);
        Assert.Equal(42, Assert.Single(next.Rows)["answer"]);
    }

    [Theory(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(IntegrationEnvironment.Enabled), SkipType = typeof(IntegrationEnvironment))]
    [InlineData("CAST(NULL AS nvarchar(1))", 12L)]
    [InlineData("CAST('' AS nvarchar(1))", 10L)]
    public async Task NullAndEmptyRows_StopAtBoundary_ZeroDisables(string expression, long boundary)
    {
        SqlExecutor executor = CreateExecutor(out _);
        CancellationToken ct = TestContext.Current.CancellationToken;
        string sql = $"SELECT {expression} AS v {ManyRows}";
        SqlQueryResult fitsOne = await executor.ExecuteQueryAsync(sql, boundary, ct);
        Assert.True(fitsOne.IsTruncated);
        Assert.Single(fitsOne.Rows);
        SqlQueryResult fitsNone = await executor.ExecuteQueryAsync(sql, boundary - 1, ct);
        Assert.True(fitsNone.IsTruncated);
        Assert.Empty(fitsNone.Rows);
        SqlQueryResult all = await executor.ExecuteQueryAsync(sql, 0, ct);
        Assert.False(all.IsTruncated);
        Assert.Equal(100, all.Rows.Count);
        SqlQueryResult single = await executor.ExecuteQueryAsync($"SELECT {expression} AS v", boundary, ct);
        Assert.False(single.IsTruncated);
        Assert.Single(single.Rows);
        await AssertUsable(executor);
    }

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(IntegrationEnvironment.Enabled), SkipType = typeof(IntegrationEnvironment))]
    public async Task LongKeysEscapesAndUnicode_AreIncludedInReaderBudget()
    {
        SqlExecutor executor = CreateExecutor(out _);
        const string key = "a_long_column_name_012345678901234567890123456789_\"\\_漢😀";
        string sql = $"SELECT N'\"\\<漢😀' AS {SqlHelpers.QuoteIdentifier(key)} {ManyRows}";
        SqlQueryResult limited = await executor.ExecuteQueryAsync(sql, 500, TestContext.Current.CancellationToken);
        Assert.True(limited.IsTruncated);
        Assert.InRange(limited.Rows.Count, 1, 4);
        foreach (Dictionary<string, object?> row in limited.Rows)
        {
            Assert.Equal("\"\\<漢😀", row[key]);
        }
        string json = JsonSerializer.Serialize(limited.Rows, ToolErrors.JsonOptions);
        Assert.True(Encoding.UTF8.GetByteCount(json) <= 500);
        SqlQueryResult all = await executor.ExecuteQueryAsync(sql, 0, TestContext.Current.CancellationToken);
        Assert.False(all.IsTruncated);
        Assert.Equal(100, all.Rows.Count);
        await AssertUsable(executor);
    }

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(IntegrationEnvironment.Enabled), SkipType = typeof(IntegrationEnvironment))]
    public async Task ExecuteSql_ConservativeEarlyStop_ReportsNoticeWhenTransportDataFits()
    {
        SqlExecutor executor = CreateExecutor(out MssqlMcpOptions options);
        options.MaxResultBytes = 37;
        SqlTools tools = new(executor, new SqlGuard(options, NullLogger<SqlGuard>.Instance), Options.Create(options), NullLogger<SqlTools>.Instance);
        CallToolResult result = await tools.ExecuteSql("SELECT 1 AS v FROM (VALUES (1),(2),(3),(4),(5)) a(n)", TestContext.Current.CancellationToken);
        Assert.False(result.IsError);
        Assert.Equal(2, result.Content.Count);
        string json = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.True(Encoding.UTF8.GetByteCount(json) < options.MaxResultBytes);
        Assert.StartsWith("[truncated]", Assert.IsType<TextContentBlock>(result.Content[1]).Text);
        await AssertUsable(executor);
    }

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(IntegrationEnvironment.Enabled), SkipType = typeof(IntegrationEnvironment))]
    public async Task RawXml_ExactBoundaryAndRefusal_LeaveSessionUsable()
    {
        SqlExecutor executor = CreateExecutor(out _);
        CancellationToken ct = TestContext.Current.CancellationToken;
        const string sql = "SELECT N'漢😀' AS v";
        string uncapped = await executor.ExecuteShowPlanXmlAsync(sql, 0, ct);
        long bytes = Encoding.UTF8.GetByteCount(uncapped);
        XDocument.Parse(uncapped);
        Assert.True(bytes > 100);
        string atBoundary = await executor.ExecuteShowPlanXmlAsync(sql, bytes, ct);
        Assert.Equal(uncapped, atBoundary);
        PlanTooLargeException refusal = await Assert.ThrowsAsync<PlanTooLargeException>(() => executor.ExecuteShowPlanXmlAsync(sql, bytes - 1, ct));
        Assert.Equal(bytes - 1, refusal.MaxBytes);
        await AssertUsable(executor);
        string disabled = await executor.ExecuteShowPlanXmlAsync(sql, 0, ct);
        Assert.Equal(uncapped, disabled);
        await AssertUsable(executor);
    }

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(IntegrationEnvironment.Enabled), SkipType = typeof(IntegrationEnvironment))]
    public async Task ExplainQuery_RawRefusal_SummaryUnaffected_DisabledRawReturnsWholeXml()
    {
        SqlExecutor executor = CreateExecutor(out MssqlMcpOptions options);
        options.MaxResultBytes = 1;
        PlanTools tools = new(executor, new SqlGuard(options, NullLogger<SqlGuard>.Instance), Options.Create(options), NullLogger<PlanTools>.Instance);
        CancellationToken ct = TestContext.Current.CancellationToken;
        CallToolResult raw = await tools.ExplainQuery("SELECT 1 AS v", "xml", ct);
        Assert.True(raw.IsError);
        Assert.Single(raw.Content);
        using (JsonDocument error = JsonDocument.Parse(Assert.IsType<TextContentBlock>(raw.Content[0]).Text))
        {
            Assert.Equal("PLAN_TOO_LARGE", error.RootElement.GetProperty("error").GetString());
            Assert.Equal(1, error.RootElement.GetProperty("max_bytes").GetInt64());
        }
        await AssertUsable(executor);
        CallToolResult summary = await tools.ExplainQuery("SELECT 1 AS v", "summary", ct);
        Assert.False(summary.IsError);
        using (JsonDocument data = JsonDocument.Parse(Assert.IsType<TextContentBlock>(summary.Content[0]).Text))
        {
            Assert.True(data.RootElement.TryGetProperty("estimated_total_cost", out _));
        }
        options.MaxResultBytes = 0;
        CallToolResult disabled = await tools.ExplainQuery("SELECT 1 AS v", "xml", ct);
        Assert.False(disabled.IsError);
        Assert.Single(disabled.Content);
        XDocument.Parse(Assert.IsType<TextContentBlock>(disabled.Content[0]).Text);
        await AssertUsable(executor);
    }
}
