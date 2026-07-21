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
/// Unit tests for the analyze_db_health tool (ADR-0016 Ops schema, ADR-0009 return shape).
/// Verifies the 5 summary objects, VLF status thresholds, fragmentation worst format,
/// and blocking count. Fakes ISqlExecutor with canned DMV results — no real DB.
/// </summary>
public class AnalyzeDbHealthTests
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

    // Helper: returns canned rows for all 5 queries in order.
    private static List<List<Dictionary<string, object?>>> CannedAllChecks(
        long sizeMb = 1234,
        long logMb = 56,
        int vlfCount = 12,
        long totalIndexes = 200,
        long fragmentedGt30 = 15,
        double maxFrag = 87.0,
        string? worst = "dbo.Orders (87%)",
        long totalStats = 150,
        long staleGt7d = 5,
        int maxStalenessDays = 30,
        int blockedSessions = 0)
    {
        return
        [
            // 1. database_size
            new()
            {
                new() { ["size_mb"] = sizeMb, ["log_mb"] = logMb },
            },
            // 2. vlf_count
            new()
            {
                new() { ["vlf_count"] = vlfCount },
            },
            // 3. index_fragmentation
            new()
            {
                new()
                {
                    ["total_indexes"] = totalIndexes,
                    ["fragmented_gt_30pct"] = fragmentedGt30,
                    ["max_fragmentation"] = maxFrag,
                    ["worst"] = worst,
                },
            },
            // 4. stats_staleness
            new()
            {
                new()
                {
                    ["total_stats"] = totalStats,
                    ["stale_gt_7d"] = staleGt7d,
                    ["max_staleness_days"] = maxStalenessDays,
                },
            },
            // 5. blocking
            new()
            {
                new() { ["blocked_sessions"] = blockedSessions },
            },
        ];
    }

    [Fact]
    public async Task AnalyzeDbHealth_ReturnsFiveChecks()
    {
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(5, doc.RootElement.GetArrayLength());

        string[] checks = doc.RootElement.EnumerateArray()
            .Select(r => r.GetProperty("check").GetString() ?? string.Empty)
            .ToArray();
        Assert.Contains("database_size", checks);
        Assert.Contains("vlf_count", checks);
        Assert.Contains("index_fragmentation", checks);
        Assert.Contains("stats_staleness", checks);
        Assert.Contains("blocking", checks);
    }

    [Fact]
    public async Task AnalyzeDbHealth_DatabaseSize_ContainsSizeAndLog()
    {
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks(sizeMb: 4096, logMb: 128);
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement sizeObj = doc.RootElement.EnumerateArray()
            .First(r => r.GetProperty("check").GetString() == "database_size");
        Assert.Equal(4096, sizeObj.GetProperty("size_mb").GetInt64());
        Assert.Equal(128, sizeObj.GetProperty("log_mb").GetInt64());
    }

    [Fact]
    public async Task AnalyzeDbHealth_VlfStatus_Ok_WhenUnder50()
    {
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks(vlfCount: 12);
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement vlfObj = doc.RootElement.EnumerateArray()
            .First(r => r.GetProperty("check").GetString() == "vlf_count");
        Assert.Equal(12, vlfObj.GetProperty("count").GetInt32());
        Assert.Equal("ok", vlfObj.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AnalyzeDbHealth_VlfStatus_Warning_When50To1000()
    {
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks(vlfCount: 200);
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement vlfObj = doc.RootElement.EnumerateArray()
            .First(r => r.GetProperty("check").GetString() == "vlf_count");
        Assert.Equal("warning", vlfObj.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AnalyzeDbHealth_VlfStatus_Critical_WhenOver1000()
    {
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks(vlfCount: 2000);
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement vlfObj = doc.RootElement.EnumerateArray()
            .First(r => r.GetProperty("check").GetString() == "vlf_count");
        Assert.Equal(2000, vlfObj.GetProperty("count").GetInt32());
        Assert.Equal("critical", vlfObj.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AnalyzeDbHealth_VlfStatus_BoundaryAt50()
    {
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks(vlfCount: 50);
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement vlfObj = doc.RootElement.EnumerateArray()
            .First(r => r.GetProperty("check").GetString() == "vlf_count");
        Assert.Equal("warning", vlfObj.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AnalyzeDbHealth_VlfStatus_BoundaryAt1000()
    {
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks(vlfCount: 1000);
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement vlfObj = doc.RootElement.EnumerateArray()
            .First(r => r.GetProperty("check").GetString() == "vlf_count");
        Assert.Equal("warning", vlfObj.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AnalyzeDbHealth_IndexFragmentation_WorstFormat()
    {
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks(worst: "dbo.Orders (87%)");
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement fragObj = doc.RootElement.EnumerateArray()
            .First(r => r.GetProperty("check").GetString() == "index_fragmentation");
        Assert.Equal(200, fragObj.GetProperty("total_indexes").GetInt64());
        Assert.Equal(15, fragObj.GetProperty("fragmented_gt_30pct").GetInt64());
        Assert.Equal("dbo.Orders (87%)", fragObj.GetProperty("worst").GetString());
    }

    [Fact]
    public async Task AnalyzeDbHealth_IndexFragmentation_NullWorst_WhenEmpty()
    {
        // When no fragmented indexes exist, the worst subquery returns NULL.
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks(
            fragmentedGt30: 0, maxFrag: 0.0, worst: null);
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement fragObj = doc.RootElement.EnumerateArray()
            .First(r => r.GetProperty("check").GetString() == "index_fragmentation");
        Assert.True(fragObj.TryGetProperty("worst", out JsonElement worstEl));
        Assert.Equal(JsonValueKind.Null, worstEl.ValueKind);
    }

    [Fact]
    public async Task AnalyzeDbHealth_StatsStaleness_ReturnsCounts()
    {
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks(
            totalStats: 150, staleGt7d: 5, maxStalenessDays: 30);
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement statsObj = doc.RootElement.EnumerateArray()
            .First(r => r.GetProperty("check").GetString() == "stats_staleness");
        Assert.Equal(150, statsObj.GetProperty("total_stats").GetInt64());
        Assert.Equal(5, statsObj.GetProperty("stale_gt_7d").GetInt64());
        Assert.Equal(30, statsObj.GetProperty("oldest_days").GetInt32());
    }

    [Fact]
    public async Task AnalyzeDbHealth_BlockingZero_ReturnsZero()
    {
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks(blockedSessions: 0);
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement blockObj = doc.RootElement.EnumerateArray()
            .First(r => r.GetProperty("check").GetString() == "blocking");
        Assert.Equal(0, blockObj.GetProperty("blocked_sessions").GetInt32());
    }

    [Fact]
    public async Task AnalyzeDbHealth_BlockingNonZero_ReturnsCount()
    {
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks(blockedSessions: 3);
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement blockObj = doc.RootElement.EnumerateArray()
            .First(r => r.GetProperty("check").GetString() == "blocking");
        Assert.Equal(3, blockObj.GetProperty("blocked_sessions").GetInt32());
    }

    [Fact]
    public async Task AnalyzeDbHealth_CrossDb_UsesQuotedPrefix()
    {
        List<string> capturedSqls = new();
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ValidDbRow(), canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: "AppDb", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        Assert.True(capturedSqls.Count >= 6);
        // Skip the first (validation) — the next 5 should all use [AppDb].sys.* prefix
        // for database-scoped DMVs. (sys.dm_exec_requests is server-scoped — it's the exception.)
        foreach (string sql in capturedSqls.Skip(1).Take(4))
        {
            Assert.Contains("[AppDb].sys.", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnalyzeDbHealth_UsesSampledMode_NotDetailed()
    {
        List<string> capturedSqls = new();
        List<List<Dictionary<string, object?>>> canned = CannedAllChecks();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(canned[0], canned[1], canned[2], canned[3], canned[4]);

        OpsTools tools = CreateTools(executor);
        await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        Assert.NotEmpty(capturedSqls);
        // The fragmentation query (3rd of 5) must use SAMPLED.
        string fragSql = capturedSqls[2];
        Assert.Contains("'SAMPLED'", fragSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'DETAILED'", fragSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeDbHealth_SqlException_ReturnsSqlError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: 208, message: "Invalid object.", severity: 16, line: 1));

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: null, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("SQL", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task AnalyzeDbHealth_CrossDb_NotFound_ReturnsConnectionError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        OpsTools tools = CreateTools(executor);
        CallToolResult result = await tools.AnalyzeDbHealth(database: "DoesNotExist", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("CONNECTION", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task AnalyzeDbHealth_ClientCancellation_Rethrows_NotTimeout()
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
            await tools.AnalyzeDbHealth(database: null, cts.Token));
    }
}
