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
/// Unit tests for the get_top_queries tool (ADR-0016 Ops schema, ADR-0009 return shape).
/// Verifies ORDER BY mapping, limit clamping, query-text truncation, and DB filter.
/// Fakes ISqlExecutor with canned DMV results — no real DB.
/// </summary>
public class GetTopQueriesTests
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

    private static List<Dictionary<string, object?>> FakeQueryRows(int count)
    {
        List<Dictionary<string, object?>> rows = new(count);
        for (int i = 0; i < count; i++)
        {
            rows.Add(new()
            {
                ["query_text"] = $"SELECT * FROM Table{i}",
                ["execution_count"] = 100 + i,
                ["total_worker_time"] = 50000L,
                ["total_elapsed_time"] = 100000L,
                ["total_logical_reads"] = 2000 + i,
                ["plan_generation_num"] = 1,
                ["creation_time"] = new DateTime(2025, 1, 1, 12, 0, 0),
            });
        }
        return rows;
    }

    private static List<Dictionary<string, object?>> LongQueryRows()
    {
        string big = new('x', 1000);
        return
        [
            new()
            {
                ["query_text"] = big,
                ["execution_count"] = 10,
                ["total_worker_time"] = 5000L,
                ["total_elapsed_time"] = 10000L,
                ["total_logical_reads"] = 200,
                ["plan_generation_num"] = 1,
                ["creation_time"] = new DateTime(2025, 1, 1, 12, 0, 0),
            },
        ];
    }

    [Fact]
    public async Task GetTopQueries_DefaultOrderBy_IsAvgCpu()
    {
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeQueryRows(5));

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetTopQueries(
            database: null, order_by: null, limit: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        Assert.NotEmpty(capturedSqls);
        string sql = capturedSqls[0];
        Assert.Contains("total_worker_time / execution_count", sql, StringComparison.Ordinal);
        Assert.Contains("DESC", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTopQueries_TotalDuration_OrderBy()
    {
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeQueryRows(2));

        OpsTools tools = CreateTools(executor);
        await tools.GetTopQueries(database: null, order_by: "total_duration", limit: null, CancellationToken.None);

        Assert.NotEmpty(capturedSqls);
        Assert.Contains("total_elapsed_time DESC", capturedSqls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTopQueries_TotalCpu_OrderBy()
    {
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeQueryRows(1));

        OpsTools tools = CreateTools(executor);
        await tools.GetTopQueries(database: null, order_by: "total_cpu", limit: null, CancellationToken.None);

        Assert.Contains("total_worker_time DESC", capturedSqls[0], StringComparison.Ordinal);
        Assert.DoesNotContain("execution_count DESC", capturedSqls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTopQueries_AvgDuration_OrderBy()
    {
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeQueryRows(1));

        OpsTools tools = CreateTools(executor);
        await tools.GetTopQueries(database: null, order_by: "avg_duration", limit: null, CancellationToken.None);

        Assert.Contains("total_elapsed_time / execution_count DESC", capturedSqls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTopQueries_TotalLogicalReads_OrderBy()
    {
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeQueryRows(1));

        OpsTools tools = CreateTools(executor);
        await tools.GetTopQueries(database: null, order_by: "total_logical_reads", limit: null, CancellationToken.None);

        Assert.Contains("total_logical_reads DESC", capturedSqls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTopQueries_ExecutionCount_OrderBy()
    {
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeQueryRows(1));

        OpsTools tools = CreateTools(executor);
        await tools.GetTopQueries(database: null, order_by: "execution_count", limit: null, CancellationToken.None);

        Assert.Contains("execution_count DESC", capturedSqls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTopQueries_UnknownOrderBy_FallsBackToAvgCpu()
    {
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeQueryRows(1));

        OpsTools tools = CreateTools(executor);
        await tools.GetTopQueries(database: null, order_by: "bogus", limit: null, CancellationToken.None);

        Assert.Contains("total_worker_time / execution_count DESC", capturedSqls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTopQueries_LimitClampedToMax100()
    {
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeQueryRows(1));

        OpsTools tools = CreateTools(executor);
        await tools.GetTopQueries(database: null, order_by: null, limit: 500, CancellationToken.None);

        Assert.NotEmpty(capturedSqls);
        Assert.Contains("TOP (@limit)", capturedSqls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTopQueries_LimitDefault10()
    {
        List<string> capturedSqls = new();
        Dictionary<string, object>? capturedParams = null;
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Do<IReadOnlyDictionary<string, object>?>(p => capturedParams = p?.ToDictionary(kv => kv.Key, kv => kv.Value)),
                Arg.Any<CancellationToken>())
            .Returns(FakeQueryRows(1));

        OpsTools tools = CreateTools(executor);
        await tools.GetTopQueries(database: null, order_by: null, limit: null, CancellationToken.None);

        Assert.NotNull(capturedParams);
        Assert.True(capturedParams.ContainsKey("limit"));
        Assert.Equal(10, capturedParams["limit"]);
    }

    [Fact]
    public async Task GetTopQueries_LimitClampedToMin1()
    {
        Dictionary<string, object>? capturedParams = null;
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Do<IReadOnlyDictionary<string, object>?>(p => capturedParams = p?.ToDictionary(kv => kv.Key, kv => kv.Value)),
                Arg.Any<CancellationToken>())
            .Returns(FakeQueryRows(1));

        OpsTools tools = CreateTools(executor);
        await tools.GetTopQueries(database: null, order_by: null, limit: -5, CancellationToken.None);

        Assert.NotNull(capturedParams);
        Assert.Equal(1, capturedParams["limit"]);
    }

    [Fact]
    public async Task GetTopQueries_LimitMax100()
    {
        Dictionary<string, object>? capturedParams = null;
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Do<IReadOnlyDictionary<string, object>?>(p => capturedParams = p?.ToDictionary(kv => kv.Key, kv => kv.Value)),
                Arg.Any<CancellationToken>())
            .Returns(FakeQueryRows(1));

        OpsTools tools = CreateTools(executor);
        await tools.GetTopQueries(database: null, order_by: null, limit: 1000, CancellationToken.None);

        Assert.NotNull(capturedParams);
        Assert.Equal(100, capturedParams["limit"]);
    }

    [Fact]
    public async Task GetTopQueries_QueryTextTruncatedTo500Chars()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(LongQueryRows());

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetTopQueries(
            database: null, order_by: null, limit: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        string queryText = doc.RootElement[0].GetProperty("query_text").GetString() ?? string.Empty;
        Assert.Equal(500, queryText.Length);
    }

    [Fact]
    public async Task GetTopQueries_DatabaseFilter_UsesDbId()
    {
        List<string> capturedSqls = new();
        Dictionary<string, object>? capturedParams = null;
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Do<IReadOnlyDictionary<string, object>?>(p => capturedParams = p?.ToDictionary(kv => kv.Key, kv => kv.Value)),
                Arg.Any<CancellationToken>())
            .Returns(ValidDbRow(), FakeQueryRows(1));

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetTopQueries(
            database: "AppDb", order_by: null, limit: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        Assert.True(capturedSqls.Count >= 2);
        string sql = capturedSqls[1]; // 0 = validation, 1 = query-stats query
        Assert.Contains("dbid = DB_ID(@database)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("[AppDb].sys.", sql, StringComparison.Ordinal);
        Assert.NotNull(capturedParams);
        Assert.True(capturedParams.ContainsKey("database"));
        Assert.Equal("AppDb", capturedParams["database"]);
    }

    [Fact]
    public async Task GetTopQueries_CurrentDb_UsesDbIdWithNoArg()
    {
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeQueryRows(1));

        OpsTools tools = CreateTools(executor);
        await tools.GetTopQueries(database: null, order_by: null, limit: null, CancellationToken.None);

        Assert.NotEmpty(capturedSqls);
        Assert.Contains("DB_ID()", capturedSqls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTopQueries_SqlException_ReturnsSqlError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: 208, message: "Invalid object.", severity: 16, line: 1));

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetTopQueries(
            database: null, order_by: null, limit: null, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("SQL", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetTopQueries_EmptyDmv_ReturnsEmptyArray()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetTopQueries(
            database: null, order_by: null, limit: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task GetTopQueries_ReturnsExpectedColumns()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeQueryRows(1));

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetTopQueries(
            database: null, order_by: null, limit: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        JsonElement row = doc.RootElement[0];
        Assert.True(row.TryGetProperty("query_text", out _));
        Assert.True(row.TryGetProperty("execution_count", out _));
        Assert.True(row.TryGetProperty("total_worker_time", out _));
        Assert.True(row.TryGetProperty("total_elapsed_time", out _));
        Assert.True(row.TryGetProperty("total_logical_reads", out _));
        Assert.True(row.TryGetProperty("plan_generation_num", out _));
        Assert.True(row.TryGetProperty("creation_time", out _));
    }
}
