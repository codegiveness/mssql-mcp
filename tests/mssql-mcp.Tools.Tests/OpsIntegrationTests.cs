using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Integration tests for analyze_indexes, get_top_queries, and analyze_db_health against
/// a real SQL Server. Tagged Category=Integration — skipped in CI via --filter Category!=Integration.
/// Requires MSSQL_CONNECTION_STRING env var pointing at a real SQL Server.
/// </summary>
[Trait("Category", "Integration")]
public class OpsIntegrationTests
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

    private static OpsTools CreateTools()
    {
        MssqlMcpOptions options = CreateOptions();
        ISqlExecutor executor = new SqlExecutor(options.ConnectionString, options.QueryTimeout,
            NullLogger<SqlExecutor>.Instance);
        return new OpsTools(executor, Options.Create(options), NullLogger<OpsTools>.Instance);
    }

    private static string GetJson(CallToolResult result)
    {
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    [Fact(Skip = "Integration test — set MSSQL_CONNECTION_STRING and run without the Category!=Integration filter.")]
    public async Task AnalyzeIndexes_RealDb_ReturnsIndexesOrEmpty()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        OpsTools tools = CreateTools();
        CallToolResult result = await tools.AnalyzeIndexes(database: null, query: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        // Empty array is valid (no missing indexes) — but must be an array, not an error.
    }

    [Fact(Skip = "Integration test — set MSSQL_CONNECTION_STRING and run without the Category!=Integration filter.")]
    public async Task GetTopQueries_RealDb_ReturnsQueries()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        OpsTools tools = CreateTools();
        CallToolResult result = await tools.GetTopQueries(
            database: null, order_by: null, limit: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        // May be empty if there's no workload — but must be an array, not an error.
    }

    [Fact(Skip = "Integration test — set MSSQL_CONNECTION_STRING and run without the Category!=Integration filter.")]
    public async Task AnalyzeDbHealth_RealDb_ReturnsFiveChecks()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        OpsTools tools = CreateTools();
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
}
