using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Unit tests for the execute_sql tool in Unrestricted mode (ADR-0001 dual-mode, ADR-0009
/// non-rowset status objects, ADR-0010 error shapes). Fakes ISqlExecutor + IGuard — no real DB.
/// </summary>
public class ExecuteSqlUnrestrictedTests
{
    private static MssqlMcpOptions UnrestrictedOptions() => new()
    {
        ConnectionString = "Server=localhost;",
        AccessMode = AccessMode.Unrestricted,
        QueryTimeout = 0,
        LogLevel = "info",
        MaxResultBytes = 10 * 1024 * 1024,
        RetryCount = 3,
        RetryIntervalMin = 2,
        RetryIntervalMax = 10,
    };

    private static SqlTools CreateTools(ISqlExecutor executor, IGuard? guard = null)
    {
        MssqlMcpOptions opts = UnrestrictedOptions();
        // Use a fake IGuard that throws if Validate is called — proves Unrestricted skips it.
        guard ??= Substitute.For<IGuard>();
        return new SqlTools(executor, guard, Options.Create(opts), NullLogger<SqlTools>.Instance);
    }

    private static string GetText(CallToolResult result)
    {
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    // ---------- DML returns status objects ----------

    [Fact]
    public async Task Unrestricted_Update_ReturnsStatusObject()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteNonQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(42);

        SqlTools tools = CreateTools(executor);
        CallToolResult result = await tools.ExecuteSql("UPDATE dbo.Users SET active = 0", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(1, doc.RootElement.GetArrayLength());

        JsonElement status = doc.RootElement[0];
        Assert.Equal("success", status.GetProperty("result").GetString());
        Assert.Equal("UPDATE", status.GetProperty("statement_type").GetString());
        Assert.Equal(42, status.GetProperty("rows_affected").GetInt32());
        // DML must NOT carry an "object" field per ADR-0009.
        Assert.False(status.TryGetProperty("object", out _));
    }

    [Fact]
    public async Task Unrestricted_Insert_ReturnsStatusObject()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteNonQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(1);

        SqlTools tools = CreateTools(executor);
        CallToolResult result = await tools.ExecuteSql(
            "INSERT INTO dbo.Users (name) VALUES ('alice')", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement status = doc.RootElement[0];
        Assert.Equal("success", status.GetProperty("result").GetString());
        Assert.Equal("INSERT", status.GetProperty("statement_type").GetString());
        Assert.Equal(1, status.GetProperty("rows_affected").GetInt32());
    }

    [Fact]
    public async Task Unrestricted_Delete_ReturnsStatusObject()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteNonQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(5);

        SqlTools tools = CreateTools(executor);
        CallToolResult result = await tools.ExecuteSql("DELETE FROM dbo.Users WHERE active = 0", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement status = doc.RootElement[0];
        Assert.Equal("success", status.GetProperty("result").GetString());
        Assert.Equal("DELETE", status.GetProperty("statement_type").GetString());
        Assert.Equal(5, status.GetProperty("rows_affected").GetInt32());
    }

    // ---------- DDL returns status objects with object name ----------

    [Fact]
    public async Task Unrestricted_CreateTable_ReturnsStatusWithObjectName()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteNonQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(-1);

        SqlTools tools = CreateTools(executor);
        CallToolResult result = await tools.ExecuteSql(
            "CREATE TABLE dbo.NewTable (id int)", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());

        JsonElement status = doc.RootElement[0];
        Assert.Equal("success", status.GetProperty("result").GetString());
        Assert.Equal("CREATE_TABLE", status.GetProperty("statement_type").GetString());
        // Object name must be present and reference the table.
        Assert.True(status.TryGetProperty("object", out JsonElement obj));
        string objName = obj.GetString() ?? string.Empty;
        Assert.Contains("NewTable", objName, StringComparison.Ordinal);
        Assert.Contains("dbo", objName, StringComparison.Ordinal);
        // DDL must NOT carry a rows_affected field per ADR-0009.
        Assert.False(status.TryGetProperty("rows_affected", out _));
    }

    [Fact]
    public async Task Unrestricted_DropTable_ReturnsStatusWithObjectName()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteNonQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(0);

        SqlTools tools = CreateTools(executor);
        CallToolResult result = await tools.ExecuteSql(
            "DROP TABLE dbo.NewTable", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement status = doc.RootElement[0];
        Assert.Equal("success", status.GetProperty("result").GetString());
        Assert.Equal("DROP_TABLE", status.GetProperty("statement_type").GetString());
        Assert.True(status.TryGetProperty("object", out JsonElement obj));
        string objName = obj.GetString() ?? string.Empty;
        Assert.Contains("NewTable", objName, StringComparison.Ordinal);
    }

    // ---------- SELECT in Unrestricted mode still returns rows ----------

    [Fact]
    public async Task Unrestricted_Select_ReturnsRowsNotStatus()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>
            {
                new() { ["x"] = 42 },
            });

        SqlTools tools = CreateTools(executor);
        CallToolResult result = await tools.ExecuteSql("SELECT 42 AS x", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal(42, doc.RootElement[0].GetProperty("x").GetInt32());

        // SELECT must NOT go through ExecuteNonQueryAsync.
        await executor.DidNotReceive().ExecuteNonQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---------- Multiple statements ----------

    [Fact]
    public async Task Unrestricted_MultipleStatements_ReturnsArrayOfStatusObjects()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // ExecuteNonQueryAsync on a multi-statement batch returns the cumulative rows affected,
        // but the contract here is: one status object per parsed statement. The tool must parse the
        // SQL into statements and emit one status object each. For the test, the fake returns 1
        // (cumulative for INSERT) — we only assert the array shape and statement types.
        executor.ExecuteNonQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(1);

        SqlTools tools = CreateTools(executor);
        CallToolResult result = await tools.ExecuteSql(
            "INSERT INTO dbo.T (a) VALUES (1); UPDATE dbo.T SET a = 2", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());

        Assert.Equal("INSERT", doc.RootElement[0].GetProperty("statement_type").GetString());
        Assert.Equal("UPDATE", doc.RootElement[1].GetProperty("statement_type").GetString());
        foreach (JsonElement s in doc.RootElement.EnumerateArray())
        {
            Assert.Equal("success", s.GetProperty("result").GetString());
        }
    }

    // ---------- No transaction wrapper ----------

    [Fact]
    public async Task Unrestricted_NoTransactionWrapper()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteNonQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(0);

        SqlTools tools = CreateTools(executor);
        await tools.ExecuteSql("UPDATE dbo.Users SET active = 1", CancellationToken.None);

        // SQL sent to executor must NOT contain the Restricted-mode transaction wrapper.
        await executor.Received(1).ExecuteNonQueryAsync(
            Arg.Is<string>(s => s != null
                                && !s.Contains("BEGIN TRANSACTION", StringComparison.Ordinal)
                                && !s.Contains("ROLLBACK TRANSACTION", StringComparison.Ordinal)
                                && s.Contains("UPDATE dbo.Users SET active = 1", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    // ---------- Guard is bypassed ----------

    [Fact]
    public async Task Unrestricted_GuardBypassed()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteNonQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(0);

        IGuard guard = Substitute.For<IGuard>();
        guard.Validate(Arg.Any<string>())
            .Throws(new InvalidOperationException("Guard.Validate must not be called in Unrestricted mode"));

        SqlTools tools = CreateTools(executor, guard);
        await tools.ExecuteSql("UPDATE dbo.Users SET active = 1", CancellationToken.None);

        guard.DidNotReceive().Validate(Arg.Any<string>());
    }

    // ---------- SQL error path ----------

    [Fact]
    public async Task Unrestricted_SqlException_ReturnsSqlError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteNonQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: 208, message: "Invalid object name 'users'.", severity: 16, line: 1));

        SqlTools tools = CreateTools(executor);
        CallToolResult result = await tools.ExecuteSql(
            "UPDATE dbo.Users SET active = 0", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("SQL", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("SQL208", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("Invalid object name 'users'.", doc.RootElement.GetProperty("message").GetString());
    }

    // ---------- Empty input still rejected ----------

    [Fact]
    public async Task Unrestricted_EmptyInput_ReturnsGuardRejection()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        SqlTools tools = CreateTools(executor);

        CallToolResult result = await tools.ExecuteSql("   ", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("GUARD_REJECTION", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("empty_batch", doc.RootElement.GetProperty("rule").GetString());
        await executor.DidNotReceive().ExecuteNonQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await executor.DidNotReceive().ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---------- explain_query remains Guarded in Unrestricted mode (no bypass) ----------

    [Fact]
    public async Task ExplainQuery_StillGuardedInUnrestrictedMode()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        MssqlMcpOptions opts = UnrestrictedOptions();
        // Real SqlGuard — proves ValidateStrict runs even with AccessMode=Unrestricted.
        IGuard guard = new SqlGuard(opts, NullLogger<SqlGuard>.Instance);
        PlanTools tools = new(executor, guard, Options.Create(opts), NullLogger<PlanTools>.Instance);

        CallToolResult result = await tools.ExplainQuery("DROP TABLE x", format: null, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("GUARD_REJECTION", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("non_select_statement", doc.RootElement.GetProperty("rule").GetString());
        await executor.DidNotReceive().ExecuteShowPlanXmlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
