using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Integration tests for list_schemas, list_objects, and get_object_details against a real
/// SQL Server. Tagged Category=Integration — skipped in CI via --filter Category!=Integration.
/// Requires MSSQL_CONNECTION_STRING env var pointing at a real SQL Server.
/// </summary>
[Trait("Category", "Integration")]
public class DiscoveryIntegrationTests
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
    public async Task ListSchemas_RealDb_ReturnsDboSchema()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        DatabaseTools tools = CreateTools();
        CallToolResult result = await tools.ListSchemas(database: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() > 0, "Expected at least one schema.");

        string[] schemaNames = doc.RootElement.EnumerateArray()
            .Select(r => r.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();
        Assert.Contains("dbo", schemaNames);
    }

    [Fact(Skip = "Integration test — set MSSQL_CONNECTION_STRING and run without the Category!=Integration filter.")]
    public async Task ListObjects_RealDb_ReturnsObjects()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        DatabaseTools tools = CreateTools();
        CallToolResult result = await tools.ListObjects(null, null, null, null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        // Any real SQL Server has at least one user object.
        Assert.True(doc.RootElement.GetArrayLength() >= 1, "Expected at least one object.");
    }

    [Fact(Skip = "Integration test — set MSSQL_CONNECTION_STRING and run without the Category!=Integration filter.")]
    public async Task GetObjectDetails_RealDb_ReturnsColumnsForSysObjectsTable()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        DatabaseTools tools = CreateTools();
        CallToolResult result = await tools.GetObjectDetails(null, "sys", "objects", null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() > 0, "Expected at least one column for sys.objects.");
        Assert.True(doc.RootElement[0].TryGetProperty("name", out _));
    }

    [Fact(Skip = "Integration test — set MSSQL_CONNECTION_STRING and run without the Category!=Integration filter.")]
    public async Task GetObjectDetails_RealDb_NotFound_ReturnsObjectNotFoundError()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        DatabaseTools tools = CreateTools();
        CallToolResult result = await tools.GetObjectDetails(
            null, "dbo", "mssql_mcp_does_not_exist_xyz", null, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("OBJECT_NOT_FOUND", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("dbo", doc.RootElement.GetProperty("schema").GetString());
        Assert.Equal("mssql_mcp_does_not_exist_xyz", doc.RootElement.GetProperty("name").GetString());
    }
}
