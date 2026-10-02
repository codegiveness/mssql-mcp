using System.Text.Json;
using Microsoft.Data.SqlClient;
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
            options.RetryCount, options.RetryIntervalMin, options.RetryIntervalMax,
            NullLogger<SqlExecutor>.Instance);
        return new DatabaseTools(executor, Options.Create(options), NullLogger<DatabaseTools>.Instance);
    }

    private static string GetJson(CallToolResult result)
    {
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    private static async Task WithOwnedTable(Func<string, string, Task> action)
    {
        string schema = $"mssql_mcp_discovery_{Guid.NewGuid():N}";
        string table = $"table_{Guid.NewGuid():N}";
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync(ct);
        using (SqlCommand createSchema = new($"CREATE SCHEMA [{schema}];", connection))
        {
            await createSchema.ExecuteNonQueryAsync(ct);
        }
        try
        {
            using (SqlCommand createTable = new(
                $"CREATE TABLE [{schema}].[{table}] (Id INT NOT NULL, Name NVARCHAR(50) NULL);", connection))
            {
                await createTable.ExecuteNonQueryAsync(ct);
            }
            await action(schema, table);
        }
        finally
        {
            using SqlCommand cleanup = new($"DROP TABLE IF EXISTS [{schema}].[{table}]; DROP SCHEMA [{schema}];", connection);
            await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(mssql_mcp.Tests.IntegrationEnvironment.Enabled), SkipType = typeof(mssql_mcp.Tests.IntegrationEnvironment))]
    public async Task ListSchemas_RealDb_ReturnsDboSchema()
    {


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

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(mssql_mcp.Tests.IntegrationEnvironment.Enabled), SkipType = typeof(mssql_mcp.Tests.IntegrationEnvironment))]
    public async Task ListObjects_RealDb_ReturnsOwnedTable()
    {
        await WithOwnedTable(async (schema, table) =>
        {
            CallToolResult result = await CreateTools().ListObjects(null, schema, "TABLE", null, TestContext.Current.CancellationToken);
            Assert.False(result.IsError ?? false);
            using JsonDocument doc = JsonDocument.Parse(GetJson(result));
            JsonElement row = Assert.Single(doc.RootElement.EnumerateArray());
            Assert.Equal(table, row.GetProperty("name").GetString());
            Assert.Equal(schema, row.GetProperty("schema").GetString());
            Assert.Equal("USER_TABLE", row.GetProperty("type").GetString());
        });
    }

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(mssql_mcp.Tests.IntegrationEnvironment.Enabled), SkipType = typeof(mssql_mcp.Tests.IntegrationEnvironment))]
    public async Task GetObjectDetails_RealDb_ReturnsOwnedTableColumns()
    {
        await WithOwnedTable(async (schema, table) =>
        {
            CallToolResult result = await CreateTools().GetObjectDetails(null, schema, table, "TABLE", TestContext.Current.CancellationToken);
            Assert.False(result.IsError ?? false);
            using JsonDocument doc = JsonDocument.Parse(GetJson(result));
            JsonElement[] columns = doc.RootElement.EnumerateArray()
                .Where(row => row.TryGetProperty("ordinal_position", out _)).ToArray();
            Assert.Equal(2, columns.Length);
            Assert.Equal("Id", columns[0].GetProperty("name").GetString());
            Assert.Equal("int", columns[0].GetProperty("system_type_name").GetString());
            Assert.Equal(1, columns[0].GetProperty("ordinal_position").GetInt32());
            Assert.Equal(0, columns[0].GetProperty("is_nullable").GetInt32());
            Assert.Equal("Name", columns[1].GetProperty("name").GetString());
            Assert.Equal("nvarchar", columns[1].GetProperty("system_type_name").GetString());
            Assert.Equal(100, columns[1].GetProperty("max_length").GetInt32());
            Assert.Equal(2, columns[1].GetProperty("ordinal_position").GetInt32());
            Assert.Equal(1, columns[1].GetProperty("is_nullable").GetInt32());
        });
    }

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(mssql_mcp.Tests.IntegrationEnvironment.Enabled), SkipType = typeof(mssql_mcp.Tests.IntegrationEnvironment))]
    public async Task GetObjectDetails_RealDb_NotFound_ReturnsObjectNotFoundError()
    {


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
