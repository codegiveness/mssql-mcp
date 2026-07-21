using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using NSubstitute;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Unit tests for the get_object_details tool — verifies columns/parameters/indexes/triggers
/// by object type, and OBJECT_NOT_FOUND on zero rows. Uses a faked ISqlExecutor.
/// </summary>
public class GetObjectDetailsTests
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

    private static List<Dictionary<string, object?>> ValidDbRow() =>
    [
        new() { ["state_desc"] = "ONLINE", ["user_access_desc"] = "MULTI_USER" },
    ];

    private static List<Dictionary<string, object?>> ObjectRow(string typeChar) =>
    [
        new() { ["type"] = typeChar, ["object_id"] = 12345L },
    ];

    private static List<Dictionary<string, object?>> Columns() =>
    [
        new()
        {
            ["name"] = "Id",
            ["system_type_name"] = "int",
            ["max_length"] = 4,
            ["precision"] = 10,
            ["scale"] = 0,
            ["is_nullable"] = 0L,
            ["is_identity"] = 1L,
            ["ordinal_position"] = 1,
        },
        new()
        {
            ["name"] = "Name",
            ["system_type_name"] = "nvarchar",
            ["max_length"] = 256,
            ["precision"] = 0,
            ["scale"] = 0,
            ["is_nullable"] = 1L,
            ["is_identity"] = 0L,
            ["ordinal_position"] = 2,
        },
    ];

    private static List<Dictionary<string, object?>> Parameters() =>
    [
        new()
        {
            ["name"] = "@OrderId",
            ["system_type_name"] = "int",
            ["max_length"] = 4,
            ["precision"] = 10,
            ["scale"] = 0,
            ["is_output"] = 0L,
            ["parameter_id"] = 1,
            ["default_value"] = null,
        },
    ];

    private static List<Dictionary<string, object?>> Indexes() =>
    [
        new()
        {
            ["name"] = "PK_Orders",
            ["type_desc"] = "CLUSTERED",
            ["is_unique"] = 1L,
            ["is_primary_key"] = 1L,
        },
    ];

    private static List<Dictionary<string, object?>> Triggers() =>
    [
        new() { ["name"] = "tr_Orders_Update", ["type_desc"] = "SQL_TRIGGER" },
    ];

    [Fact]
    public async Task GetObjectDetails_Table_ReturnsColumns()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // First call: sys.objects lookup (returns table type U)
        // Then: columns, indexes, triggers
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(
                ObjectRow("U"),   // object type lookup
                Columns(),         // columns
                Indexes(),         // indexes
                Triggers());       // triggers

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetObjectDetails(null, "dbo", "Orders", null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        // columns (2) + indexes (1) + triggers (1) = 4 rows total
        Assert.Equal(4, doc.RootElement.GetArrayLength());
        // First row should be a column.
        Assert.Equal("Id", doc.RootElement[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetObjectDetails_Table_UsesQuotedDbPrefix()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ValidDbRow(), ObjectRow("U"), Columns(), Indexes(), Triggers());

        DatabaseTools tools = CreateTools(executor);
        await tools.GetObjectDetails("AppDb", "dbo", "Orders", null, CancellationToken.None);

        // All subsequent queries (after validation) must use [AppDb].sys.* prefix.
        await executor.Received(4).ExecuteQueryAsync(
            Arg.Is<string>(s => s != null && s.Contains("[AppDb].sys.", StringComparison.Ordinal)),
            Arg.Any<IReadOnlyDictionary<string, object>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetObjectDetails_View_ReturnsColumnsButNoIndexes()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ObjectRow("V"), Columns());

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetObjectDetails(null, "dbo", "ActiveOrders", null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        // View: only columns (2), no indexes, no triggers.
        Assert.Equal(2, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task GetObjectDetails_Procedure_ReturnsParameters()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ObjectRow("P"), Parameters());

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetObjectDetails(null, "dbo", "GetOrder", null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal("@OrderId", doc.RootElement[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetObjectDetails_Function_ReturnsParameters()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ObjectRow("FN"), Parameters());

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetObjectDetails(null, "dbo", "ComputeTotal", null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task GetObjectDetails_NotFound_ReturnsObjectNotFoundError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetObjectDetails(null, "dbo", "Nonexistent", null, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("OBJECT_NOT_FOUND", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("dbo", doc.RootElement.GetProperty("schema").GetString());
        Assert.Equal("Nonexistent", doc.RootElement.GetProperty("name").GetString());
        // database field — null when current DB is used (null coalesces).
        Assert.True(doc.RootElement.TryGetProperty("database", out _));
    }

    [Fact]
    public async Task GetObjectDetails_TableAlsoReturnsIndexes()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ObjectRow("U"), Columns(), Indexes(), Triggers());

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetObjectDetails(null, "dbo", "Orders", null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        // Find an index row (PK_Orders)
        bool foundIndex = false;
        foreach (JsonElement row in doc.RootElement.EnumerateArray())
        {
            if (row.TryGetProperty("name", out JsonElement name) && name.GetString() == "PK_Orders"
                && row.TryGetProperty("type_desc", out JsonElement td) && td.GetString() == "CLUSTERED")
            {
                foundIndex = true;
                break;
            }
        }
        Assert.True(foundIndex, "Expected an index row for the table.");
    }

    [Fact]
    public async Task GetObjectDetails_TableAlsoReturnsTriggers()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ObjectRow("U"), Columns(), Indexes(), Triggers());

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.GetObjectDetails(null, "dbo", "Orders", null, CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetJson(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        bool foundTrigger = false;
        foreach (JsonElement row in doc.RootElement.EnumerateArray())
        {
            if (row.TryGetProperty("name", out JsonElement name) && name.GetString() == "tr_Orders_Update")
            {
                foundTrigger = true;
                break;
            }
        }
        Assert.True(foundTrigger, "Expected a trigger row for the table.");
    }

    [Fact]
    public async Task GetObjectDetails_TypeFilter_PassedThroughToLookup()
    {
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ObjectRow("U"));

        DatabaseTools tools = CreateTools(executor);
        await tools.GetObjectDetails(null, "dbo", "Orders", "TABLE", CancellationToken.None);

        // First captured SQL is the sys.objects lookup (the one that should carry the type filter).
        Assert.NotEmpty(capturedSqls);
        string lookupSql = capturedSqls[0];
        Assert.Contains("type='U'", lookupSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetObjectDetails_DetailQueries_UseObjectIdParameter_NotObject_IdFunction()
    {
        // Regression test for Oracle review finding: OBJECT_ID(@qualifiedName) resolves in
        // the current DB context, not the target DB. Fix: pass object_id from the lookup
        // query as @objectId parameter to detail queries.
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ValidDbRow(), ObjectRow("U"), Columns(), Indexes(), Triggers());

        DatabaseTools tools = CreateTools(executor);
        await tools.GetObjectDetails("AppDb", "dbo", "Orders", null, CancellationToken.None);

        // Verify detail queries use @objectId, not OBJECT_ID(...)
        Assert.True(capturedSqls.Count >= 4, $"Expected at least 4 SQL calls, got {capturedSqls.Count}");
        foreach (string sql in capturedSqls.Skip(2)) // skip validation + lookup
        {
            Assert.DoesNotContain("OBJECT_ID", sql, StringComparison.Ordinal);
            Assert.Contains("@objectId", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetObjectDetails_LookupQuery_ReturnsObjectId()
    {
        // Verify the lookup query SELECTs object_id alongside type.
        List<string> capturedSqls = new();
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Do<string>(s => capturedSqls.Add(s)),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ValidDbRow(), ObjectRow("U"), Columns(), Indexes(), Triggers());

        DatabaseTools tools = CreateTools(executor);
        await tools.GetObjectDetails("AppDb", "dbo", "Orders", null, CancellationToken.None);

        // The lookup query (2nd call after validation) should select object_id.
        Assert.True(capturedSqls.Count >= 2);
        string lookupSql = capturedSqls[1];
        Assert.Contains("object_id", lookupSql, StringComparison.Ordinal);
    }
}
