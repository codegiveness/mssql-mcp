using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;
using mssql_mcp.Core.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// PoC tests written by security-research poc-engineer-b to verify (or falsify) two candidate
/// findings:
///   A2: SqlException.Message leaks verbatim into CallToolResult.Content — PasswordObfuscator
///       only runs on log sinks (stderr/file), NOT on the JSON-RPC response payload.
///   A3: SqlHelpers.ValidateDatabaseAsync has no denylist for system DBs (master, tempdb, model,
///       msdb) even though ListDatabasesSql hides them from list_databases.
/// </summary>
public class PocLeakTests
{
    // ---------- A2: SqlException.Message leak ----------

    /// <summary>
    /// A2 REPRODUCTION: A non-transient SqlException whose Message contains a connection-string
    /// fragment (simulating what could happen if a SqlException ever wraps a connection-string
    /// bearing message — e.g. some Microsoft.Data.SqlClient error paths, or a future change).
    /// The message flows verbatim into CallToolResult.Content with NO PasswordObfuscator applied.
    /// </summary>
    [Fact]
    public async Task A2_SqlErrorMessage_WithPasswordFragment_ObfuscatedInCallToolResult()
    {
        // Arrange — a fake SqlException with a message that mimics a leaked connection string.
        const string leakyMessage =
            "Login failed for user 'sa'. Connection: Server=localhost;User Id=sa;Password=Hunter2!;Encrypt=True;";
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: 18456, message: leakyMessage, severity: 14, line: 1));

        MssqlMcpOptions opts = RestrictedOptions();
        SqlGuard guard = new(opts, NullLogger<SqlGuard>.Instance);
        SqlTools tools = new(executor, guard, Options.Create(opts), NullLogger<SqlTools>.Instance);

        // Act — execute_sql in Restricted mode. The Guard accepts "SELECT 1" and wraps it;
        // the executor throws the simulated SqlException; ToolErrors.SqlError now applies
        // PasswordObfuscator before serializing into the JSON payload.
        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        // Assert — the connection-string password fragment is OBFUSCATED in the
        // CallToolResult.Content[0].Text.
        Assert.True(result.IsError ?? false);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        string json = Assert.IsType<TextContentBlock>(result.Content[0]).Text;

        Assert.Contains("Password=***;", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=Hunter2!;", json, StringComparison.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("SQL", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("SQL18456", doc.RootElement.GetProperty("code").GetString());
        string message = doc.RootElement.GetProperty("message").GetString() ?? string.Empty;
        Assert.Equal(PasswordObfuscator.Obfuscate(leakyMessage), message);
    }

    /// <summary>
    /// A2 CONTRAST: ConnectionValidator.ValidateAsync (the --validate CLI path) DOES call
    /// PasswordObfuscator.Obfuscate on ex.Message. This proves the omission in ToolErrors is
    /// an asymmetry, not a deliberate global invariant — the codebase knows how to obfuscate
    /// exception messages, it just doesn't do it for the tool-response path.
    /// </summary>
    [Fact]
    public async Task A2_ConnectionValidator_AndToolErrors_BothObfuscate()
    {
        const string leaky = "Server=localhost;User Id=sa;Password=Secret123;Encrypt=True;";
        string obfuscated = PasswordObfuscator.Obfuscate(leaky);
        Assert.NotEqual(leaky, obfuscated);
        Assert.Contains("Password=***;", obfuscated);

        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: 18456, message: leaky, severity: 14, line: 1));

        MssqlMcpOptions opts = RestrictedOptions();
        SqlTools tools = new(executor, new SqlGuard(opts, NullLogger<SqlGuard>.Instance),
            Options.Create(opts), NullLogger<SqlTools>.Instance);

        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);
        string json = Assert.IsType<TextContentBlock>(result.Content[0]).Text;

        Assert.Contains("Password=***;", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=Secret123;", json, StringComparison.Ordinal);
    }

    // ---------- A3: system DB denylist ----------

    /// <summary>
    /// A3 REPRODUCTION: ValidateDatabaseAsync accepts 'master' (database_id=1) as valid
    /// because it only checks existence + ONLINE + MULTI_USER — there is NO denylist.
    /// This proves the per-tool `database:` parameter on list_schemas / list_objects /
    /// get_object_details / OpsTools can be pointed at master, tempdb, model, or msdb.
    /// (Compare: DatabaseTools.ListDatabasesSql hides database_id &lt;= 4 from list_databases.)
    /// </summary>
    [Fact]
    public async Task A3_ValidateDatabaseAsync_AcceptsMaster_NoDenylist()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // Simulate what sys.databases returns for master: state_desc=ONLINE, user_access_desc=MULTI_USER.
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>
            {
                new() { ["state_desc"] = "ONLINE", ["user_access_desc"] = "MULTI_USER" },
            });

        DatabaseValidationResult result =
            await SqlHelpers.ValidateDatabaseAsync(executor, "master", CancellationToken.None);

        Assert.True(result.Valid, "master must be accepted — no denylist exists");
        Assert.Null(result.Error);

        // Falsification target: did ValidateDatabaseAsync reject any of the system DBs?
        // It only checks (1) exists, (2) ONLINE, (3) MULTI_USER — none of those filter master.
    }

    /// <summary>
    /// A3 EXTENDED: every system database (master, tempdb, model, msdb) passes validation
    /// when sys.databases reports them ONLINE + MULTI_USER. No name-based denylist is consulted.
    /// </summary>
    [Theory]
    [InlineData("master")]
    [InlineData("tempdb")]
    [InlineData("model")]
    [InlineData("msdb")]
    public async Task A3_ValidateDatabaseAsync_AcceptsAllSystemDbs(string systemDb)
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>
            {
                new() { ["state_desc"] = "ONLINE", ["user_access_desc"] = "MULTI_USER" },
            });

        DatabaseValidationResult result =
            await SqlHelpers.ValidateDatabaseAsync(executor, systemDb, CancellationToken.None);

        Assert.True(result.Valid, $"{systemDb} must be accepted — no denylist exists");
    }

    /// <summary>
    /// A3 CONTRAST: ListDatabasesSql hides database_id &lt;= 4. This proves the codebase
    /// is aware of system DBs and intentionally hides them from discovery, but does NOT
    /// enforce the same boundary on the per-tool `database:` parameter.
    /// </summary>
    [Fact]
    public void A3_ListDatabasesSql_HidesSystemDbs_ButValidateDoesNot()
    {
        // Read the literal SQL string via reflection (it's a private const on DatabaseTools).
        System.Reflection.FieldInfo? field = typeof(DatabaseTools).GetField(
            "ListDatabasesSql",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        string sql = (string)field!.GetValue(null)!;

        // list_databases filters out system DBs (database_id <= 4).
        Assert.Contains("database_id > 4", sql, StringComparison.Ordinal);

        // ValidateDatabaseAsync's SQL (private const ValidateDatabaseSql on SqlHelpers) does NOT.
        System.Reflection.FieldInfo? validateField = typeof(SqlHelpers).GetField(
            "ValidateDatabaseSql",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(validateField);
        string validateSql = (string)validateField!.GetValue(null)!;
        Assert.DoesNotContain("database_id", validateSql, StringComparison.Ordinal);
        Assert.DoesNotContain("master", validateSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tempdb", validateSql, StringComparison.OrdinalIgnoreCase);
    }

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
}
