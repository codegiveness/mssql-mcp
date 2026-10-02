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
/// Verifies limit clamping, query-text truncation, empty-result JSON, and SQL error mapping.
/// SQL ordering/filtering require live DMV execution, not source-string assertions.
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
    public async Task GetTopQueries_LimitClampedToMin1()
    {
        Dictionary<string, object>? capturedParams = null;
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Do<IReadOnlyDictionary<string, object>?>(p => capturedParams = p?.ToDictionary(kv => kv.Key, kv => kv.Value)), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new SqlQueryResult(FakeQueryRows(1), false));

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
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Do<IReadOnlyDictionary<string, object>?>(p => capturedParams = p?.ToDictionary(kv => kv.Key, kv => kv.Value)), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new SqlQueryResult(FakeQueryRows(1), false));

        OpsTools tools = CreateTools(executor);
        await tools.GetTopQueries(database: null, order_by: null, limit: 1000, CancellationToken.None);

        Assert.NotNull(capturedParams);
        Assert.Equal(100, capturedParams["limit"]);
    }

    [Fact]
    public async Task GetTopQueries_QueryTextTruncatedTo500Chars()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object>?>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new SqlQueryResult(LongQueryRows(), false));

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
    public async Task GetTopQueries_SqlException_ReturnsSqlError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object>?>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
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
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object>?>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new SqlQueryResult(new List<Dictionary<string, object?>>(), false));

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetTopQueries(
            database: null, order_by: null, limit: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }
}
