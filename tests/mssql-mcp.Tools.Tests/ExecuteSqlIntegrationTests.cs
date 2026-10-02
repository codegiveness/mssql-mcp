using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Integration tests for execute_sql against a real SQL Server.
/// Tagged Category=Integration — skipped in CI via --filter Category!=Integration.
/// Requires MSSQL_CONNECTION_STRING env var pointing at a real SQL Server.
/// </summary>
[Trait("Category", "Integration")]
public class ExecuteSqlIntegrationTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING");

    private static SqlTools CreateRestrictedTools()
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
        IGuard guard = new SqlGuard(options, NullLogger<SqlGuard>.Instance);
        return new SqlTools(executor, guard, Options.Create(options), NullLogger<SqlTools>.Instance);
    }

    private static string GetJson(CallToolResult result)
    {
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(mssql_mcp.Tests.IntegrationEnvironment.Enabled), SkipType = typeof(mssql_mcp.Tests.IntegrationEnvironment))]
    public async Task ExecuteSql_SelectFromOwnedTable_ReturnsInsertedRow()
    {
        string tableName = $"mssql_mcp_execute_test_{Guid.NewGuid():N}";
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync(ct);
        using (SqlCommand create = new(
            $"CREATE TABLE [dbo].[{tableName}] (Id INT NOT NULL, Name NVARCHAR(50) NOT NULL);", connection))
        {
            await create.ExecuteNonQueryAsync(ct);
        }
        try
        {
            using (SqlCommand insert = new($"INSERT INTO [dbo].[{tableName}] (Id, Name) VALUES (7, N'fixture');", connection))
            {
                await insert.ExecuteNonQueryAsync(ct);
            }
            SqlTools tools = CreateRestrictedTools();
            CallToolResult result = await tools.ExecuteSql($"SELECT Id, Name FROM [dbo].[{tableName}]", ct);
            Assert.False(result.IsError ?? false);
            using JsonDocument doc = JsonDocument.Parse(GetJson(result));
            JsonElement row = Assert.Single(doc.RootElement.EnumerateArray());
            Assert.Equal(7, row.GetProperty("Id").GetInt32());
            Assert.Equal("fixture", row.GetProperty("Name").GetString());
        }
        finally
        {
            using SqlCommand drop = new($"DROP TABLE [dbo].[{tableName}];", connection);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(mssql_mcp.Tests.IntegrationEnvironment.Enabled), SkipType = typeof(mssql_mcp.Tests.IntegrationEnvironment))]
    public async Task ExecuteSql_DropTableInRestricted_ReturnsGuardRejection_AndDoesNotExecute()
    {


        SqlTools tools = CreateRestrictedTools();
        CallToolResult result = await tools.ExecuteSql(
            "DROP TABLE mssql_mcp_should_not_exist_xyz",
            CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("GUARD_REJECTION", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("non_select_statement", doc.RootElement.GetProperty("rule").GetString());

        // Verify the table doesn't exist (i.e. the DROP was not executed).
        // Use a separate SELECT through execute_sql itself — if the table never existed, this returns
        // a SQL error (208) instead of a rowset, which is itself the proof.
        SqlTools tools2 = CreateRestrictedTools();
        CallToolResult checkResult = await tools2.ExecuteSql(
            "SELECT TOP 1 name FROM sys.tables WHERE name = 'mssql_mcp_should_not_exist_xyz'",
            CancellationToken.None);
        Assert.False(checkResult.IsError ?? false);
        using JsonDocument checkDoc = JsonDocument.Parse(GetJson(checkResult));
        Assert.Equal(0, checkDoc.RootElement.GetArrayLength());
    }

    // ---------- Unrestricted mode (integration) ----------

    private static SqlTools CreateUnrestrictedTools()
    {
        MssqlMcpOptions options = new()
        {
            ConnectionString = ConnectionString!,
            AccessMode = AccessMode.Unrestricted,
            QueryTimeout = 0,
            LogLevel = "info",
            MaxResultBytes = 10 * 1024 * 1024,
            RetryCount = 3,
            RetryIntervalMin = 2,
            RetryIntervalMax = 10,
        };
        ISqlExecutor executor = new SqlExecutor(options.ConnectionString, options.QueryTimeout,
            options.RetryCount, options.RetryIntervalMin, options.RetryIntervalMax,
            NullLogger<SqlExecutor>.Instance);
        IGuard guard = new SqlGuard(options, NullLogger<SqlGuard>.Instance);
        return new SqlTools(executor, guard, Options.Create(options), NullLogger<SqlTools>.Instance);
    }

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(mssql_mcp.Tests.IntegrationEnvironment.Enabled), SkipType = typeof(mssql_mcp.Tests.IntegrationEnvironment))]
    public async Task Unrestricted_CreateAndDropTable_RealDb_ReturnsStatusObjects()
    {


        SqlTools tools = CreateUnrestrictedTools();

        CallToolResult createResult = await tools.ExecuteSql(
            "CREATE TABLE dbo.McpTest (id int)", CancellationToken.None);
        Assert.False(createResult.IsError ?? false);
        string createJson = GetJson(createResult);
        using JsonDocument createDoc = JsonDocument.Parse(createJson);
        Assert.Equal(1, createDoc.RootElement.GetArrayLength());
        Assert.Equal("success", createDoc.RootElement[0].GetProperty("result").GetString());
        Assert.Equal("CREATE_TABLE", createDoc.RootElement[0].GetProperty("statement_type").GetString());
        Assert.True(createDoc.RootElement[0].TryGetProperty("object", out _));

        try
        {
            CallToolResult dropResult = await tools.ExecuteSql(
                "DROP TABLE dbo.McpTest", CancellationToken.None);
            Assert.False(dropResult.IsError ?? false);
            string dropJson = GetJson(dropResult);
            using JsonDocument dropDoc = JsonDocument.Parse(dropJson);
            Assert.Equal(1, dropDoc.RootElement.GetArrayLength());
            Assert.Equal("success", dropDoc.RootElement[0].GetProperty("result").GetString());
            Assert.Equal("DROP_TABLE", dropDoc.RootElement[0].GetProperty("statement_type").GetString());
        }
        finally
        {
            // Cleanup if the DROP above failed — best-effort, ignore errors.
            try
            {
                await tools.ExecuteSql("IF OBJECT_ID('dbo.McpTest') IS NOT NULL DROP TABLE dbo.McpTest",
                    CancellationToken.None);
            }
            catch
            {
                // Swallow — cleanup is best-effort.
            }
        }
    }
}
