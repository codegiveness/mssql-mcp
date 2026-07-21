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
/// Cross-cutting tests for ADR-0010 error handling: all 6 error classes, transient
/// classification, severity-25 surfacing, no-stack-trace invariant. Exercises the
/// error path through the public tool surface so tests survive refactors of the
/// internal <c>ToolErrors</c> helper.
/// </summary>
public class ErrorHandlingTests
{
    private const int DefaultTimeoutSeconds = 30;
    private const int DefaultTimeoutMs = DefaultTimeoutSeconds * 1000;

    private static MssqlMcpOptions RestrictedOptions(int timeoutSeconds = DefaultTimeoutSeconds) => new()
    {
        ConnectionString = "Server=localhost;",
        AccessMode = AccessMode.Restricted,
        QueryTimeout = timeoutSeconds,
        LogLevel = "info",
        MaxResultBytes = 10 * 1024 * 1024,
        RetryCount = 3,
        RetryIntervalMin = 2,
        RetryIntervalMax = 10,
    };

    private static string GetText(CallToolResult result)
    {
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    private static SqlTools CreateSqlTools(ISqlExecutor executor, MssqlMcpOptions? options = null, IGuard? guard = null)
    {
        MssqlMcpOptions opts = options ?? RestrictedOptions();
        guard ??= new SqlGuard(opts, NullLogger<SqlGuard>.Instance);
        return new SqlTools(executor, guard, Options.Create(opts), NullLogger<SqlTools>.Instance);
    }

    // ---------- Transient classification (CONNECTION vs SQL) ----------

    [Theory]
    [InlineData(4060)]
    [InlineData(40197)]
    [InlineData(40501)]
    [InlineData(40613)]
    [InlineData(41839)]
    [InlineData(49918)]
    [InlineData(49919)]
    [InlineData(49920)]
    [InlineData(11001)]
    public async Task SqlError_TransientErrorNumber_ReturnsConnectionClass(int transientNumber)
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: transientNumber, message: "Transient blip.", severity: 16, line: 1));

        SqlTools tools = CreateSqlTools(executor);
        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetText(result));
        Assert.Equal("CONNECTION", doc.RootElement.GetProperty("error").GetString());
        // ADR-0010: CONNECTION detail carries "{message}. Retries exhausted."
        string detail = doc.RootElement.GetProperty("detail").GetString() ?? string.Empty;
        Assert.Contains("Retries exhausted", detail, StringComparison.Ordinal);
        Assert.Contains("Transient blip", detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(208)]   // Invalid object name — classic non-transient
    [InlineData(207)]   // Invalid column
    [InlineData(512)]   // Subquery returned more than one value
    public async Task SqlError_NonTransientErrorNumber_ReturnsSqlClass(int nonTransientNumber)
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: nonTransientNumber, message: "Hard SQL error.", severity: 16, line: 1));

        SqlTools tools = CreateSqlTools(executor);
        CallToolResult result = await tools.ExecuteSql("SELECT * FROM users", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetText(result));
        Assert.Equal("SQL", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal($"SQL{nonTransientNumber}", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(16, doc.RootElement.GetProperty("severity").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("line").GetInt32());
        // procedure may be null per ADR-0010 — JsonElement.ValueKind is Null when present.
        Assert.True(doc.RootElement.TryGetProperty("procedure", out JsonElement proc));
        Assert.Equal(JsonValueKind.Null, proc.ValueKind);
    }

    // ---------- Severity-25 surfaced, not fatal ----------

    [Fact]
    public async Task SqlError_Severity25_ReturnsSqlErrorNotFatal()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: 824, message: "SQL Server detected a logical consistency-based I/O error.", severity: 25, line: 1));

        SqlTools tools = CreateSqlTools(executor);
        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        // Severity-25 is surfaced to the Agent as a SQL error — NOT process exit, NOT INTERNAL.
        Assert.True(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetText(result));
        Assert.Equal("SQL", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("SQL824", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(25, doc.RootElement.GetProperty("severity").GetInt32());

        // Process is still alive (we got here). The test runner would have exited if
        // Environment.Exit had been called — xUnit catches the call as a test failure
        // via CannotUnloadAppDomainException, so this assertion is implicit in reaching here.
        Assert.False(Environment.HasShutdownStarted, "Environment must not be shutting down after severity-25.");
    }

    // ---------- INTERNAL never includes stack traces ----------

    [Fact]
    public async Task InternalError_NeverIncludesStackTrace()
    {
        InvalidOperationException exWithStack = MakeExceptionWithStack();
        string exMessage = exWithStack.Message;

        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(exWithStack);

        SqlTools tools = CreateSqlTools(executor);
        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = GetText(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("INTERNAL", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("InvalidOperationException", doc.RootElement.GetProperty("exception_type").GetString());

        // detail must carry the exception message verbatim — never ex.ToString().
        Assert.Equal(exMessage, doc.RootElement.GetProperty("detail").GetString());

        // The stack trace marker ("at " at the start of a stack frame line) must NOT appear
        // anywhere in the JSON response sent to the Agent (ADR-0011).
        Assert.DoesNotContain("\n   at ", json, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", json, StringComparison.Ordinal);
        Assert.DoesNotContain(exWithStack.GetType().Namespace ?? "<no-namespace>", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Forces a real stack trace onto the exception by throwing and catching — without this
    /// the exception's StackTrace property returns null and the test would be vacuous.
    /// </summary>
    private static InvalidOperationException MakeExceptionWithStack()
    {
        try
        {
            throw new InvalidOperationException("Kaboom — something internal failed.");
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    // ---------- TIMEOUT includes timeout_ms ----------

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task TimeoutError_IncludesTimeoutMs(int timeoutSeconds)
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // Plain OperationCanceledException (NOT client cancellation) → command timeout path.
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        SqlTools tools = CreateSqlTools(executor, RestrictedOptions(timeoutSeconds));
        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetText(result));
        Assert.Equal("TIMEOUT", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(timeoutSeconds * 1000, doc.RootElement.GetProperty("timeout_ms").GetInt32());
        Assert.NotNull(doc.RootElement.GetProperty("detail").GetString());
    }

    // ---------- GUARD_REJECTION includes rule ----------

    [Theory]
    [InlineData("DROP TABLE users", "non_select_statement")]
    [InlineData("SELECT * INTO new_tbl FROM sys.objects", "select_into")]
    public async Task GuardRejection_IncludesRuleField(string sql, string expectedRule)
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        SqlTools tools = CreateSqlTools(executor);

        CallToolResult result = await tools.ExecuteSql(sql, CancellationToken.None);

        Assert.True(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetText(result));
        Assert.Equal("GUARD_REJECTION", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(expectedRule, doc.RootElement.GetProperty("rule").GetString());
        Assert.NotNull(doc.RootElement.GetProperty("detail").GetString());

        // statement_type and position are required keys per ADR-0010 (may be "" / null).
        Assert.True(doc.RootElement.TryGetProperty("statement_type", out _));
        Assert.True(doc.RootElement.TryGetProperty("position", out JsonElement pos));
        // position is null for parse-layer rejections that have no token coordinates.
        Assert.True(pos.ValueKind is JsonValueKind.Null or JsonValueKind.Object);

        // Guard rejection must never reach the executor.
        await executor.DidNotReceive().ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---------- CONNECTION includes "Retries exhausted" ----------

    [Fact]
    public async Task ConnectionError_Transient_IncludesRetriesExhaustedMessage()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // 40613 = "Cannot open database '...' requested by the login. The database is currently
        // in the restore state / unavailable. Try again later." — Microsoft's canonical transient.
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: 40613, message: "Cannot open database.", severity: 16, line: 1));

        SqlTools tools = CreateSqlTools(executor);
        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetText(result));
        Assert.Equal("CONNECTION", doc.RootElement.GetProperty("error").GetString());
        string detail = doc.RootElement.GetProperty("detail").GetString() ?? string.Empty;
        Assert.Contains("Retries exhausted", detail, StringComparison.Ordinal);
    }

    // ---------- OBJECT_NOT_FOUND shape ----------

    [Fact]
    public async Task ObjectNotFound_IncludesAllLookupFields()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // get_object_details first runs the lookup query — return zero rows → OBJECT_NOT_FOUND.
        // database: null avoids triggering cross-DB validation (separate code path).
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        MssqlMcpOptions opts = RestrictedOptions();
        DatabaseTools tools = new(executor, Options.Create(opts), NullLogger<DatabaseTools>.Instance);

        CallToolResult result = await tools.GetObjectDetails(database: null, schema: "dbo", name: "Orders", type: "TABLE", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetText(result));
        Assert.Equal("OBJECT_NOT_FOUND", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("dbo", doc.RootElement.GetProperty("schema").GetString());
        Assert.Equal("Orders", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("TABLE", doc.RootElement.GetProperty("type").GetString());
        // database: null avoids triggering cross-DB validation (separate code path).
        // ADR-0010: database field is required, "" when current DB is used.
        Assert.True(doc.RootElement.TryGetProperty("database", out JsonElement db));
        Assert.Equal(string.Empty, db.GetString());
    }

    // ---------- Cross-tool invariant: every error return sets IsError = true ----------

    [Theory]
    [InlineData(208)]   // SQL class
    [InlineData(40613)] // CONNECTION class
    public async Task EveryErrorReturn_SetsIsErrorTrue(int sqlErrorNumber)
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: sqlErrorNumber, message: "Some failure.", severity: 16, line: 1));

        SqlTools tools = CreateSqlTools(executor);
        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        Assert.True(result.IsError, "All error returns must set IsError=true per ADR-0010.");
    }

    // ---------- Cross-tool invariant: client cancellation rethrows (not a timeout) ----------

    [Fact]
    public async Task ClientCancellation_Rethrows_NotTimeoutError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        using CancellationTokenSource cts = new();
        cts.Cancel();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException(cts.Token));

        SqlTools tools = CreateSqlTools(executor);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await tools.ExecuteSql("SELECT 1", cts.Token));
    }
}
