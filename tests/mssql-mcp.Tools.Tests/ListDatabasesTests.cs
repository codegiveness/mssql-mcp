using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using NSubstitute;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Unit tests for the list_databases tool — verifies JSON shape and is_current handling
/// using a faked ISqlExecutor (no real DB).
/// </summary>
public class ListDatabasesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static DatabaseTools CreateTools(ISqlExecutor executor, MssqlMcpOptions? options = null)
    {
        MssqlMcpOptions opts = options ?? new MssqlMcpOptions
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
        IOptions<MssqlMcpOptions> optionsWrapper = Options.Create(opts);
        return new DatabaseTools(executor, optionsWrapper, NullLogger<DatabaseTools>.Instance);
    }

    private static List<Dictionary<string, object?>> FakeDatabases() =>
    [
        new()
        {
            ["name"] = "AppDb",
            ["database_id"] = 5,
            ["state_desc"] = "ONLINE",
            ["is_current"] = 1L,
        },
        new()
        {
            ["name"] = "ReportingDb",
            ["database_id"] = 6,
            ["state_desc"] = "ONLINE",
            ["is_current"] = 0L,
        },
        new()
        {
            ["name"] = "ArchiveDb",
            ["database_id"] = 7,
            ["state_desc"] = "ONLINE",
            ["is_current"] = 0L,
        },
    ];

    [Fact]
    public async Task ListDatabases_ReturnsJsonArrayOfObjects()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FakeDatabases());

        DatabaseTools tools = CreateTools(executor);
        string json = await tools.ListDatabases(CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(3, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ListDatabases_IncludesIsCurrentField()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FakeDatabases());

        DatabaseTools tools = CreateTools(executor);
        string json = await tools.ListDatabases(CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(json);
        foreach (JsonElement row in doc.RootElement.EnumerateArray())
        {
            Assert.True(row.TryGetProperty("is_current", out JsonElement _));
        }
    }

    [Fact]
    public async Task ListDatabases_MarksCurrentDatabaseWithIsCurrentTrue()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FakeDatabases());

        DatabaseTools tools = CreateTools(executor);
        string json = await tools.ListDatabases(CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement firstRow = doc.RootElement[0];
        Assert.Equal("AppDb", firstRow.GetProperty("name").GetString());
        // bit coerces to JSON number 1/0 per ADR-0009.
        Assert.Equal(1, firstRow.GetProperty("is_current").GetInt64());
    }

    [Fact]
    public async Task ListDatabases_NonCurrentDatabases_HaveIsCurrentFalse()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FakeDatabases());

        DatabaseTools tools = CreateTools(executor);
        string json = await tools.ListDatabases(CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement[] rows = doc.RootElement.EnumerateArray().ToArray();
        Assert.Equal(0, rows[1].GetProperty("is_current").GetInt64());
        Assert.Equal(0, rows[2].GetProperty("is_current").GetInt64());
    }

    [Fact]
    public async Task ListDatabases_EmptyResult_ReturnsEmptyArray()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        DatabaseTools tools = CreateTools(executor);
        string json = await tools.ListDatabases(CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ListDatabases_IncludesName_DatabaseId_StateDesc()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FakeDatabases());

        DatabaseTools tools = CreateTools(executor);
        string json = await tools.ListDatabases(CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement firstRow = doc.RootElement[0];
        Assert.Equal("AppDb", firstRow.GetProperty("name").GetString());
        Assert.Equal(5, firstRow.GetProperty("database_id").GetInt32());
        Assert.Equal("ONLINE", firstRow.GetProperty("state_desc").GetString());
    }

    [Fact]
    public async Task ListDatabases_PassesCancellationToken()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FakeDatabases());

        DatabaseTools tools = CreateTools(executor);
        using CancellationTokenSource cts = new();
        await tools.ListDatabases(cts.Token);

        await executor.Received(1).ExecuteQueryAsync(
            Arg.Any<string>(),
            Arg.Is<CancellationToken>(ct => ct == cts.Token));
    }
}
