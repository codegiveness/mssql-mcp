using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using NSubstitute;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Unit tests for the list_objects tool — verifies defaults, type-enum mapping, limit clamping,
/// ms_shipped filter, and truncation notice prepended when limit is hit.
/// </summary>
public class ListObjectsTests
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

    private static List<Dictionary<string, object?>> FakeObjects(int count)
    {
        List<Dictionary<string, object?>> rows = new(count);
        for (int i = 0; i < count; i++)
        {
            rows.Add(new()
            {
                ["name"] = $"Object{i}",
                ["schema"] = "dbo",
                ["type"] = "USER_TABLE",
            });
        }
        return rows;
    }

    private static List<Dictionary<string, object?>> ValidDbRow() =>
    [
        new() { ["state_desc"] = "ONLINE", ["user_access_desc"] = "MULTI_USER" },
    ];

    [Fact]
    public async Task ListObjects_DefaultLimit_ReturnsObjects_NoTruncation()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeObjects(5));

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.ListObjects(null, null, null, null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(5, doc.RootElement.GetArrayLength());
        Assert.False(doc.RootElement[0].TryGetProperty("truncated", out _));
    }

    [Fact]
    public async Task ListObjects_LimitHit_PrependsTruncationNotice()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeObjects(1000));

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.ListObjects(null, null, null, limit: 1000, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(1001, doc.RootElement.GetArrayLength());
        JsonElement first = doc.RootElement[0];
        Assert.True(first.TryGetProperty("truncated", out JsonElement t));
        Assert.True(t.GetBoolean());
        Assert.Equal(1000, first.GetProperty("returned").GetInt64());
        Assert.Contains("truncated", first.GetProperty("note").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListObjects_TypeFilterTable_MapsToCharU()
    {
        string? capturedSql = null;
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSql = s),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeObjects(1));

        DatabaseTools tools = CreateTools(executor);
        await tools.ListObjects(null, null, "TABLE", null, CancellationToken.None);

        Assert.NotNull(capturedSql);
        Assert.Contains("type='U'", capturedSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListObjects_TypeFilterView_MapsToCharV()
    {
        string? capturedSql = null;
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSql = s),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeObjects(1));

        DatabaseTools tools = CreateTools(executor);
        await tools.ListObjects(null, null, "VIEW", null, CancellationToken.None);

        Assert.NotNull(capturedSql);
        Assert.Contains("type='V'", capturedSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListObjects_TypeFilterProcedure_MapsToCharsP_PC()
    {
        string? capturedSql = null;
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSql = s),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeObjects(1));

        DatabaseTools tools = CreateTools(executor);
        await tools.ListObjects(null, null, "PROCEDURE", null, CancellationToken.None);

        Assert.NotNull(capturedSql);
        Assert.Contains("type IN ('P','PC')", capturedSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListObjects_TypeFilterFunction_MapsToCharsFnIfTfFsFt()
    {
        string? capturedSql = null;
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSql = s),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeObjects(1));

        DatabaseTools tools = CreateTools(executor);
        await tools.ListObjects(null, null, "FUNCTION", null, CancellationToken.None);

        Assert.NotNull(capturedSql);
        Assert.Contains("type IN ('FN','IF','TF','FS','FT')", capturedSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListObjects_LimitClampedToMax5000()
    {
        string? capturedSql = null;
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSql = s),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeObjects(1));

        DatabaseTools tools = CreateTools(executor);
        await tools.ListObjects(null, null, null, limit: 99999, CancellationToken.None);

        Assert.NotNull(capturedSql);
        Assert.Contains("TOP (@limit)", capturedSql, StringComparison.Ordinal);
        await executor.Received(1).ExecuteQueryAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, object>?>(p => p != null && Convert.ToInt32(p["limit"]) == 5000),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListObjects_LimitClampedToMin1()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeObjects(1));

        DatabaseTools tools = CreateTools(executor);
        await tools.ListObjects(null, null, null, limit: -5, CancellationToken.None);

        await executor.Received(1).ExecuteQueryAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, object>?>(p => p != null && Convert.ToInt32(p["limit"]) == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListObjects_DefaultLimit_Is1000()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeObjects(1));

        DatabaseTools tools = CreateTools(executor);
        await tools.ListObjects(null, null, null, null, CancellationToken.None);

        await executor.Received(1).ExecuteQueryAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, object>?>(p => p != null && Convert.ToInt32(p["limit"]) == 1000),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListObjects_ExcludesMsShippedObjects()
    {
        string? capturedSql = null;
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSql = s),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(FakeObjects(1));

        DatabaseTools tools = CreateTools(executor);
        await tools.ListObjects(null, null, null, null, CancellationToken.None);

        Assert.NotNull(capturedSql);
        Assert.Contains("is_ms_shipped=0", capturedSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListObjects_SchemaFilter_UsesSchemaIdFunction()
    {
        string? capturedSql = null;
        IReadOnlyDictionary<string, object>? capturedParams = null;
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSql = s),
                Arg.Do<IReadOnlyDictionary<string, object>?>(p => capturedParams = p),
                Arg.Any<CancellationToken>())
            .Returns(FakeObjects(1));

        DatabaseTools tools = CreateTools(executor);
        await tools.ListObjects(null, schema: "dbo", null, null, CancellationToken.None);

        Assert.NotNull(capturedSql);
        Assert.Contains("schema_id=SCHEMA_ID(@schema)", capturedSql, StringComparison.Ordinal);
        Assert.NotNull(capturedParams);
        Assert.Equal("dbo", capturedParams!["schema"]);
    }

    [Fact]
    public async Task ListObjects_SpecifiedDb_UsesQuotedIdentifierInQuery()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // First call: ValidateDatabase (queries sys.databases with @database param).
        // Second call: list_objects query (uses [AppDb].sys.objects).
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(
                ci => ValidDbRow(),
                ci => FakeObjects(1));

        DatabaseTools tools = CreateTools(executor);
        await tools.ListObjects("AppDb", null, null, null, CancellationToken.None);

        await executor.Received(1).ExecuteQueryAsync(
            Arg.Is<string>(s => s != null && s.Contains("[AppDb].sys.objects", StringComparison.Ordinal)),
            Arg.Any<IReadOnlyDictionary<string, object>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListObjects_InvalidDb_ReturnsError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.ListObjects("MissingDb", null, null, null, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("CONNECTION", doc.RootElement.GetProperty("error").GetString());
    }
}
