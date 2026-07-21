using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Unit tests for the analyze_indexes tool (ADR-0016 Ops schema, ADR-0009 return shape).
/// Fakes ISqlExecutor with canned DMV results — no real DB.
/// </summary>
public class AnalyzeIndexesTests
{
    private static MssqlMcpOptions TestOptions() => new()
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

    private static OpsTools CreateTools(ISqlExecutor executor)
        => new(executor, Options.Create(TestOptions()), NullLogger<OpsTools>.Instance);

    private static string GetJson(CallToolResult result)
    {
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    private static List<Dictionary<string, object?>> ValidDbRow() =>
    [
        new() { ["state_desc"] = "ONLINE", ["user_access_desc"] = "MULTI_USER" },
    ];

    private static List<Dictionary<string, object?>> MissingIndexRows(int count)
    {
        List<Dictionary<string, object?>> rows = new(count);
        for (int i = 0; i < count; i++)
        {
            rows.Add(new()
            {
                ["object"] = $"[dbo].[Table{i}]",
                ["equality_columns"] = "CustomerId",
                ["inequality_columns"] = "OrderDate",
                ["included_columns"] = "TotalAmount",
                ["user_seeks"] = 100 + i,
                ["user_scans"] = 50,
                ["avg_user_impact"] = 95.5,
                ["avg_total_user_cost"] = 0.5,
                ["improvement_measure"] = (100 + i) * 95.5 * 0.5,
            });
        }
        return rows;
    }

    [Fact]
    public async Task AnalyzeIndexes_WorkloadWide_ReturnsMissingIndexes()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(MissingIndexRows(3));

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeIndexes(database: null, query: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(3, doc.RootElement.GetArrayLength());
        Assert.Equal("[dbo].[Table0]", doc.RootElement[0].GetProperty("object").GetString());
        Assert.Equal("CustomerId", doc.RootElement[0].GetProperty("equality_columns").GetString());
    }

    [Fact]
    public async Task AnalyzeIndexes_WorkloadWide_IncludesTop20AndImprovementMeasure()
    {
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(MissingIndexRows(3));

        OpsTools tools = CreateTools(executor);
        await tools.AnalyzeIndexes(database: null, query: null, CancellationToken.None);

        Assert.NotEmpty(capturedSqls);
        string sql = capturedSqls[0];
        Assert.Contains("TOP (20)", sql, StringComparison.Ordinal);
        Assert.Contains("improvement_measure", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("dm_exec_query_stats", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeIndexes_WithQuery_FiltersByPlanHandle()
    {
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(MissingIndexRows(1));

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeIndexes(
            database: null, query: "SELECT * FROM dbo.Orders WHERE CustomerId = @c", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        Assert.NotEmpty(capturedSqls);
        string sql = capturedSqls[0];
        // Per-query SQL must filter by plan_handle via sys.dm_exec_query_stats.
        Assert.Contains("sys.dm_exec_query_stats", sql, StringComparison.Ordinal);
        Assert.Contains("plan_handle", sql, StringComparison.Ordinal);
        Assert.Contains("@query", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeIndexes_CrossDb_ValidatesAndUsesQuotedPrefix()
    {
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ValidDbRow(), MissingIndexRows(1));

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeIndexes(
            database: "AppDb", query: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        Assert.True(capturedSqls.Count >= 2);
        string indexSql = capturedSqls[1]; // 0 = validation, 1 = missing-index query
        Assert.Contains("[AppDb].sys.dm_db_missing_index_details", indexSql, StringComparison.Ordinal);
        Assert.Contains("[AppDb].sys.dm_db_missing_index_groups", indexSql, StringComparison.Ordinal);
        Assert.Contains("[AppDb].sys.dm_db_missing_index_group_stats", indexSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeIndexes_SqlException_ReturnsSqlError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: 208, message: "Invalid object name 'sys.dm_db_missing_index_details'.", severity: 16, line: 1));

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeIndexes(database: null, query: null, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("SQL", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("SQL208", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnalyzeIndexes_CrossDb_NotFound_ReturnsConnectionError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>()); // validation: 0 rows = not found

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeIndexes(
            database: "DoesNotExist", query: null, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("CONNECTION", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task AnalyzeIndexes_LimitedToTop20()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(MissingIndexRows(25));

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeIndexes(database: null, query: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(20, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task AnalyzeIndexes_EmptyDmv_ReturnsEmptyArray()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeIndexes(database: null, query: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task AnalyzeIndexes_ClientCancellation_Rethrows_NotTimeout()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        using CancellationTokenSource cts = new();
        cts.Cancel();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException(cts.Token));

        OpsTools tools = CreateTools(executor);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await tools.AnalyzeIndexes(database: null, query: null, cts.Token));
    }
}
