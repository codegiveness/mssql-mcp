using System.Reflection;
using System.Text.Json;
using Microsoft.Data.SqlClient;
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
/// Unit tests for the execute_sql tool in Restricted mode (ADR-0010 error shapes, ADR-0009 return shape).
/// Fakes ISqlExecutor + IGuard — no real DB.
/// </summary>
public class ExecuteSqlTests
{
    private const int DefaultTimeoutMs = 30_000;

    private static MssqlMcpOptions RestrictedOptions() => new()
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

    /// <summary>
    /// Builds a SqlTools instance with faked executor and (optionally) faked guard.
    /// In Restricted mode, the real SqlGuard is used by default — tests that need to force
    /// a rejection pass a fake IGuard.
    /// </summary>
    private static SqlTools CreateTools(
        ISqlExecutor executor,
        MssqlMcpOptions options,
        IGuard? guard = null)
    {
        guard ??= new SqlGuard(options, NullLogger<SqlGuard>.Instance);
        return new SqlTools(executor, guard, Options.Create(options), NullLogger<SqlTools>.Instance);
    }

    private static string GetText(CallToolResult result)
    {
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    // ---------- Success path ----------

    [Fact]
    public async Task ExecuteSql_SelectOne_ReturnsJsonArray_AndIsErrorFalse()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // SELECT 1 produces a single column with empty name → key "" per ADR-0009.
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>
            {
                new() { [""] = 1 },
            });

        SqlTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal(1, doc.RootElement[0].GetProperty("").GetInt32());
    }

    [Fact]
    public async Task ExecuteSql_GuardAccept_PassesWrappedSqlToExecutor()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        SqlTools tools = CreateTools(executor, RestrictedOptions());
        await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        // The wrapped SQL must contain the sentinel + BEGIN TRAN / ROLLBACK per ADR-0007.
        await executor.Received(1).ExecuteQueryAsync(
            Arg.Is<string>(s => s != null
                                && s.Contains("/* mssql-mcp */", StringComparison.Ordinal)
                                && s.Contains("BEGIN TRANSACTION", StringComparison.Ordinal)
                                && s.Contains("ROLLBACK TRANSACTION", StringComparison.Ordinal)
                                && s.Contains("SELECT 1", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteSql_EmptyResult_ReturnsEmptyArray_AndIsErrorFalse()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        SqlTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExecuteSql("SELECT TOP 0 * FROM sys.objects", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        Assert.Equal("[]", GetText(result));
    }

    // ---------- Guard rejections ----------

    [Fact]
    public async Task ExecuteSql_GuardRejectsDropTable_ReturnsGuardRejection_AndIsErrorTrue()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        SqlTools tools = CreateTools(executor, RestrictedOptions());

        CallToolResult result = await tools.ExecuteSql("DROP TABLE nonexistent", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("GUARD_REJECTION", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("non_select_statement", doc.RootElement.GetProperty("rule").GetString());
        Assert.NotNull(doc.RootElement.GetProperty("detail").GetString());

        // Guard rejection must never reach the executor.
        await executor.DidNotReceive().ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteSql_GuardRejectsSelectInto_ReturnsGuardRejection_WithSelectIntoRule()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        SqlTools tools = CreateTools(executor, RestrictedOptions());

        CallToolResult result = await tools.ExecuteSql("SELECT * INTO NewTable FROM sys.objects", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("GUARD_REJECTION", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("select_into", doc.RootElement.GetProperty("rule").GetString());
        await executor.DidNotReceive().ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---------- SQL error path ----------

    [Fact]
    public async Task ExecuteSql_SqlException_ReturnsSqlError_AndIsErrorTrue()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: 208, message: "Invalid object name 'users'.", severity: 16, line: 1));

        SqlTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExecuteSql("SELECT * FROM users", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("SQL", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("SQL208", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("Invalid object name 'users'.", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal(16, doc.RootElement.GetProperty("severity").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("line").GetInt32());
    }

    // ---------- Timeout path ----------

    [Fact]
    public async Task ExecuteSql_OperationCanceled_ReturnsTimeoutError_AndIsErrorTrue()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        SqlTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("TIMEOUT", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(DefaultTimeoutMs, doc.RootElement.GetProperty("timeout_ms").GetInt32());
        Assert.NotNull(doc.RootElement.GetProperty("detail").GetString());
    }

    // ---------- Unrestricted mode ----------

    [Fact]
    public async Task ExecuteSql_UnrestrictedMode_SkipsGuard_ExecutesRawSql()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>
            {
                new() { ["x"] = 42 },
            });

        // Use a fake IGuard that throws if called — proves Unrestricted skips it.
        IGuard guard = Substitute.For<IGuard>();
        guard.Validate(Arg.Any<string>()).Throws(new InvalidOperationException("Guard should not be called in Unrestricted mode"));

        MssqlMcpOptions opts = UnrestrictedOptions();
        SqlTools tools = CreateTools(executor, opts, guard);
        CallToolResult result = await tools.ExecuteSql("UPDATE dbo.Users SET active = 0", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal(42, doc.RootElement[0].GetProperty("x").GetInt32());

        // Unrestricted mode executes SQL as-is (no BEGIN TRAN / ROLLBACK wrapper).
        await executor.Received(1).ExecuteQueryAsync(
            Arg.Is<string>(s => s == "UPDATE dbo.Users SET active = 0"),
            Arg.Any<CancellationToken>());

        guard.DidNotReceive().Validate(Arg.Any<string>());
    }

    // ---------- Empty input ----------

    [Fact]
    public async Task ExecuteSql_EmptyInput_ReturnsGuardRejection_RuleEmptyBatch()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        SqlTools tools = CreateTools(executor, RestrictedOptions());

        CallToolResult result = await tools.ExecuteSql("   ", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("GUARD_REJECTION", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("empty_batch", doc.RootElement.GetProperty("rule").GetString());
        await executor.DidNotReceive().ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---------- Null column value preserved (ADR-0009: NULL → JSON null) ----------

    [Fact]
    public async Task ExecuteSql_NullColumn_PreservedInJson()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>
            {
                new() { ["a"] = 1, ["b"] = null },
            });

        SqlTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExecuteSql("SELECT 1 AS a, NULL AS b", CancellationToken.None);

        Assert.False(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal(1, doc.RootElement[0].GetProperty("a").GetInt32());
        // The key "b" MUST be present with JSON null (ADR-0009), NOT omitted.
        Assert.True(doc.RootElement[0].TryGetProperty("b", out JsonElement bVal));
        Assert.Equal(JsonValueKind.Null, bVal.ValueKind);
    }

    // ---------- INTERNAL error (catch-all) ----------

    [Fact]
    public async Task ExecuteSql_UnexpectedException_ReturnsInternalError_AndIsErrorTrue()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Something went wrong inside the executor"));

        SqlTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("INTERNAL", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("InvalidOperationException", doc.RootElement.GetProperty("exception_type").GetString());
        Assert.NotNull(doc.RootElement.GetProperty("detail").GetString());
    }

    // ---------- Client cancellation is NOT a timeout ----------

    [Fact]
    public async Task ExecuteSql_ClientCancellation_Rethrows_NotTimeout()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException(cts.Token));

        SqlTools tools = CreateTools(executor, RestrictedOptions());
        // Client cancellation should rethrow, NOT return a TIMEOUT error.
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await tools.ExecuteSql("SELECT 1", cts.Token));
    }

    // ---------- SqlException with fallback path ----------

    [Fact]
    public async Task ExecuteSql_SqlException_UsesFirstErrorProperties()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: 208, message: "Invalid object name 'users'.", severity: 16, line: 1));

        SqlTools tools = CreateTools(executor, RestrictedOptions());
        CallToolResult result = await tools.ExecuteSql("SELECT * FROM users", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("SQL", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("SQL208", doc.RootElement.GetProperty("code").GetString());
    }
}

/// <summary>
/// Helper that constructs a real <see cref="SqlException"/> via its non-public constructor.
/// SqlException has no public constructor — tests need to fabricate one to exercise the
/// execute_sql SQL-error branch without a real database.
/// </summary>
internal static class SqlExceptionFactory
{
    public static SqlException Create(int number, string message, byte severity = 16, int line = 1, string? procedure = null)
    {
        // Use the 9-arg internal SqlError ctor, then add it to an internal SqlErrorCollection,
        // then construct SqlException via its 4-arg internal ctor.
        Type sqlErrorType = typeof(SqlError);
        Type sqlErrorCollectionType = typeof(SqlErrorCollection);

        // Pick the 9-arg ctor: (infoNumber, errorState, errorClass, server, errorMessage, procedure, lineNumber, win32ErrorCode, exception).
        ConstructorInfo? errorCtor = sqlErrorType.GetConstructors(
            BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(c => c.GetParameters().Length == 9)
            ?? throw new InvalidOperationException("SqlError 9-arg ctor not found.");

        object error = errorCtor.Invoke(new object?[]
        {
            number,        // infoNumber
            (byte)1,       // errorState
            severity,      // errorClass
            "localhost",   // server
            message,       // errorMessage
            procedure,     // procedure
            line,          // lineNumber
            0,             // win32ErrorCode
            null,          // exception
        });

        ConstructorInfo? collectionCtor = sqlErrorCollectionType.GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)
            ?? throw new InvalidOperationException("SqlErrorCollection parameterless ctor not found.");
        object collection = collectionCtor.Invoke(null);

        MethodInfo? addMethod = sqlErrorCollectionType.GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("SqlErrorCollection.Add not found.");
        addMethod.Invoke(collection, new[] { error });

        Type sqlExceptionType = typeof(SqlException);
        ConstructorInfo? exCtor = sqlExceptionType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(c =>
            {
                ParameterInfo[] p = c.GetParameters();
                return p.Length == 4
                    && p[0].ParameterType == typeof(string)
                    && p[1].ParameterType == typeof(SqlErrorCollection)
                    && p[2].ParameterType == typeof(Exception)
                    && p[3].ParameterType == typeof(Guid);
            })
            ?? throw new InvalidOperationException("SqlException 4-arg ctor not found.");

        return (SqlException)exCtor.Invoke(new object?[]
        {
            message,
            collection,
            null,
            Guid.Empty,
        });
    }
}
