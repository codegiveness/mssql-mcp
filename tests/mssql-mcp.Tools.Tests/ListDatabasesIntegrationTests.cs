using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Integration tests for list_databases against a real SQL Server.
/// Tagged Category=Integration — skipped in CI via --filter Category!=Integration.
/// Requires MSSQL_CONNECTION_STRING env var pointing at a real SQL Server.
/// </summary>
[Trait("Category", "Integration")]
public class ListDatabasesIntegrationTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING");

    private static DatabaseTools CreateTools()
    {
        MssqlMcpOptions options = new()
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
        ISqlExecutor executor = new SqlExecutor(options.ConnectionString, options.QueryTimeout,
            NullLogger<SqlExecutor>.Instance);
        return new DatabaseTools(executor, Options.Create(options), NullLogger<DatabaseTools>.Instance);
    }

    private static string GetJson(CallToolResult result)
    {
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    [Fact(Skip = "Integration test — set MSSQL_CONNECTION_STRING and run without the Category!=Integration filter.")]
    public async Task ListDatabases_RealServer_ExcludesSystemDatabases()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        DatabaseTools tools = CreateTools();
        CallToolResult result = await tools.ListDatabases(CancellationToken.None);
        using JsonDocument doc = JsonDocument.Parse(GetJson(result));

        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() > 0, "Expected at least one database.");

        string[] names = doc.RootElement.EnumerateArray()
            .Select(r => r.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain("master", names);
        Assert.DoesNotContain("tempdb", names);
        Assert.DoesNotContain("model", names);
        Assert.DoesNotContain("msdb", names);
    }

    [Fact(Skip = "Integration test — set MSSQL_CONNECTION_STRING and run without the Category!=Integration filter.")]
    public async Task ListDatabases_RealServer_CurrentDatabaseHasIsCurrentTrue()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        DatabaseTools tools = CreateTools();
        CallToolResult result = await tools.ListDatabases(CancellationToken.None);
        using JsonDocument doc = JsonDocument.Parse(GetJson(result));

        Assert.True(doc.RootElement.GetArrayLength() > 0);
        bool anyCurrent = doc.RootElement.EnumerateArray()
            .Any(r => r.GetProperty("is_current").GetInt64() == 1);
        Assert.True(anyCurrent, "Expected at least one database with is_current=1.");
    }
}
