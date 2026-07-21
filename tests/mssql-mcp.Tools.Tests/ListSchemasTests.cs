using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using NSubstitute;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Unit tests for the list_schemas tool — verifies default-DB path, cross-DB validated path,
/// and DB-validation failure path. Uses a faked ISqlExecutor (no real DB).
/// </summary>
public class ListSchemasTests
{
    private static DatabaseTools CreateTools(ISqlExecutor executor)
    {
        MssqlMcpOptions opts = new()
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
        return new DatabaseTools(executor, Options.Create(opts), NullLogger<DatabaseTools>.Instance);
    }

    private static string GetJson(CallToolResult result)
    {
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    private static List<Dictionary<string, object?>> FakeSchemas() =>
    [
        new() { ["name"] = "dbo", ["schema_id"] = 1L },
        new() { ["name"] = "guest", ["schema_id"] = 2L },
        new() { ["name"] = "INFORMATION_SCHEMA", ["schema_id"] = 3L },
        new() { ["name"] = "sys", ["schema_id"] = 4L },
    ];

    private static List<Dictionary<string, object?>> ValidDbRow() =>
    [
        new() { ["state_desc"] = "ONLINE", ["user_access_desc"] = "MULTI_USER" },
    ];

    [Fact]
    public async Task ListSchemas_CurrentDb_ReturnsSchemas()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FakeSchemas());

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.ListSchemas(database: null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(4, doc.RootElement.GetArrayLength());
        Assert.Equal("dbo", doc.RootElement[0].GetProperty("name").GetString());
        Assert.Equal(1, doc.RootElement[0].GetProperty("schema_id").GetInt64());
    }

    [Fact]
    public async Task ListSchemas_CurrentDb_DoesNotCallParameterizedOverload()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FakeSchemas());

        DatabaseTools tools = CreateTools(executor);
        await tools.ListSchemas(database: null, CancellationToken.None);

        await executor.Received(1).ExecuteQueryAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await executor.DidNotReceive().ExecuteQueryAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSchemas_SpecifiedDb_ValidatesAndReturns()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // First call: ValidateDatabaseAsync (parameterized)
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ValidDbRow());
        // Second call: list_schemas query (no params)
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FakeSchemas());

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.ListSchemas(database: "AppDb", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(4, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ListSchemas_SpecifiedDb_UsesQuotedIdentifierInQuery()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ValidDbRow());
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FakeSchemas());

        DatabaseTools tools = CreateTools(executor);
        await tools.ListSchemas(database: "AppDb", CancellationToken.None);

        // The list_schemas query must use [AppDb].sys.schemas, NOT raw AppDb.
        await executor.Received(1).ExecuteQueryAsync(
            Arg.Is<string>(s => s != null && s.Contains("[AppDb].sys.schemas", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSchemas_InvalidDb_ReturnsError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // ValidateDatabase returns zero rows.
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.ListSchemas(database: "MissingDb", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("CONNECTION", doc.RootElement.GetProperty("error").GetString());
        Assert.Contains("MissingDb", doc.RootElement.GetProperty("detail").GetString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListSchemas_OfflineDb_ReturnsError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns([new() { ["state_desc"] = "OFFLINE", ["user_access_desc"] = "MULTI_USER" }]);

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.ListSchemas(database: "AppDb", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("CONNECTION", doc.RootElement.GetProperty("error").GetString());
        Assert.Contains("not online", doc.RootElement.GetProperty("detail").GetString() ?? string.Empty, StringComparison.Ordinal);
    }
}
