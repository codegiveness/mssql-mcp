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
/// PoC tests written by security-research poc-engineer-b (Phase 2) to verify or falsify
/// candidate findings from the hunter pass:
///   AHD-1 (SH-1): Cross-DB read in Restricted mode via 3-part names (SchemaObjectName
///                  visitor only rejects Count >= 4).
///   AHD-2 / SH-3:  Credential leak via SqlErrorOrConnection transient-error path
///                  (ToolErrors.cs:261 uses raw ex.Message, no PasswordObfuscator).
///   AHD-3 / SH-2:  Credential leak via ToolErrors.Internal (ToolErrors.cs:294 uses raw
///                  ex.Message, no PasswordObfuscator).
/// Each test reproduces the finding with toy inputs against the in-memory code path
/// (no live SQL Server required). Unsafe-to-run candidates are documented inline.
/// </summary>
public class PocLeakTests2
{
    // ---------- AHD-1: Cross-DB read via 3-part name in Restricted mode ----------

    /// <summary>
    /// AHD-1 REPRODUCTION: SqlGuard.Validate ACCEPTS a SELECT against a 3-part name
    /// (OtherDb.dbo.Users). The Visit(SchemaObjectName) override only rejects when
    /// Identifiers.Count >= 4 (linked-server 4-part names). A 3-part name has Count == 3
    /// and passes the guard. In Restricted mode the SELECT is wrapped in
    /// BEGIN TRAN ... ROLLBACK TRAN, but the resultset is still streamed to the Agent
    /// before ROLLBACK executes — the rollback only undoes writes, not reads.
    ///
    /// This proves an Agent in Restricted mode can read rows from any database the
    /// connection's login has SELECT permission on, even though list_databases hides
    /// system DBs (database_id <= 4) from discovery.
    /// </summary>
    [Fact]
    public void AHD1_SqlGuard_AcceptsThreePartName_CrossDbReadInRestricted()
    {
        var guard = CreateGuard();

        GuardResult result = guard.Validate("SELECT * FROM OtherDb.dbo.Users");

        // The guard ACCEPTS — no rejection fired.
        Assert.True(result.Accepted,
            $"Expected 3-part name to be accepted, got rejection: {result.Rejection?.Rule} — {result.Rejection?.Detail}");
        Assert.NotNull(result.WrappedSql);

        // The wrapped SQL contains BEGIN TRANSACTION + the verbatim 3-part SELECT.
        Assert.Contains("BEGIN TRANSACTION", result.WrappedSql, StringComparison.Ordinal);
        Assert.Contains("OtherDb.dbo.Users", result.WrappedSql, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION", result.WrappedSql, StringComparison.Ordinal);
    }

    /// <summary>
    /// AHD-1 CONTRAST: The same visitor DOES reject 4-part names. This proves the gap
    /// is specifically at the 3-part boundary — the code knows about linked-server
    /// references but not cross-database references within the same instance.
    /// </summary>
    [Fact]
    public void AHD1_Contrast_FourPartName_IsRejected()
    {
        var guard = CreateGuard();

        GuardResult result = guard.Validate("SELECT * FROM [server].[db].[dbo].[table]");

        Assert.False(result.Accepted);
        Assert.NotNull(result.Rejection);
        Assert.Equal("four_part_name", result.Rejection!.Rule);
    }

    /// <summary>
    /// AHD-1 EXTENDED: 3-part name with brackets ([OtherDb].[dbo].[Users]) is also
    /// accepted — the parser normalizes brackets before counting Identifiers.
    /// </summary>
    [Fact]
    public void AHD1_SqlGuard_AcceptsBracketedThreePartName()
    {
        var guard = CreateGuard();

        GuardResult result = guard.Validate("SELECT * FROM [OtherDb].[dbo].[Users]");

        Assert.True(result.Accepted,
            $"Expected bracketed 3-part name to be accepted, got rejection: {result.Rejection?.Rule}");
    }

    // ---------- AHD-2 / SH-3: Credential leak via transient-error path ----------

    /// <summary>
    /// AHD-2 REGRESSION: A transient SqlException (error number 4060 — cannot open
    /// database) whose Message contains a connection-string password is now OBFUSCATED
    /// in the CallToolResult via SqlErrorOrConnection's transient branch
    /// (ToolErrors.cs:261 → ConnectionError($"{PasswordObfuscator.Obfuscate(ex.Message)} Retries exhausted.")).
    ///
    /// Previously this path leaked the raw password. The fix applies PasswordObfuscator
    /// to the transient branch, matching the non-transient SqlError path.
    /// </summary>
    [Fact]
    public async Task AHD2_TransientSqlException_ObfuscatesPasswordInConnectionError()
    {
        // 4060 is in TransientErrorNumbers — routes to the ConnectionError branch.
        const string leakyMessage =
            "Cannot open database \"secret\" requested by the login. Login failed. " +
            "Connection: Server=prod-db;User Id=sa;Password=Hunter2!;Encrypt=True;";
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: 4060, message: leakyMessage, severity: 11, line: 1));

        MssqlMcpOptions opts = RestrictedOptions();
        SqlTools tools = new(executor, new SqlGuard(opts, NullLogger<SqlGuard>.Instance),
            Options.Create(opts), NullLogger<SqlTools>.Instance);

        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = Assert.IsType<TextContentBlock>(result.Content[0]).Text;

        // The password is obfuscated — raw value must NOT appear.
        Assert.DoesNotContain("Password=Hunter2!;", json, StringComparison.Ordinal);
        Assert.Contains("Password=***;", json, StringComparison.Ordinal);

        // Confirm the error class is CONNECTION (transient branch).
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("CONNECTION", doc.RootElement.GetProperty("error").GetString());
    }

    /// <summary>
    /// AHD-2 CONTRAST: A non-transient SqlException (error 18456 — login failed) routes
    /// to ToolErrors.SqlError which DOES obfuscate. This proves the leak is specific to
    /// the transient branch, not a global invariant.
    /// </summary>
    [Fact]
    public async Task AHD2_Contrast_NonTransientSqlException_Obfuscates()
    {
        const string leakyMessage =
            "Login failed for user 'sa'. Server=localhost;User Id=sa;Password=Hunter2!;Encrypt=True;";
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // 18456 is NOT in TransientErrorNumbers — routes to SqlError (obfuscated).
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(SqlExceptionFactory.Create(number: 18456, message: leakyMessage, severity: 14, line: 1));

        MssqlMcpOptions opts = RestrictedOptions();
        SqlTools tools = new(executor, new SqlGuard(opts, NullLogger<SqlGuard>.Instance),
            Options.Create(opts), NullLogger<SqlTools>.Instance);

        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        string json = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        Assert.Contains("Password=***;", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=Hunter2!;", json, StringComparison.Ordinal);
    }

    // ---------- AHD-3 / SH-2: Credential leak via ToolErrors.Internal ----------

    /// <summary>
    /// AHD-3 REGRESSION: A non-SqlException (generic Exception) whose Message contains
    /// a connection-string password is now OBFUSCATED in the CallToolResult via
    /// ToolErrors.Internal (ToolErrors.cs:294 → Detail = PasswordObfuscator.Obfuscate(ex.Message)).
    ///
    /// Previously this path leaked the raw password. The fix applies PasswordObfuscator
    /// to the internal error path, matching the SqlError path.
    /// </summary>
    [Fact]
    public async Task AHD3_GenericException_ObfuscatesPasswordInInternalError()
    {
        const string leakyMessage =
            "Unexpected state during connect. Config: Server=prod-db;User Id=sa;Password=Secret123;Encrypt=True;";
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException(leakyMessage));

        MssqlMcpOptions opts = RestrictedOptions();
        SqlTools tools = new(executor, new SqlGuard(opts, NullLogger<SqlGuard>.Instance),
            Options.Create(opts), NullLogger<SqlTools>.Instance);

        CallToolResult result = await tools.ExecuteSql("SELECT 1", CancellationToken.None);

        Assert.True(result.IsError ?? false);
        string json = Assert.IsType<TextContentBlock>(result.Content[0]).Text;

        // The password is obfuscated — raw value must NOT appear.
        Assert.DoesNotContain("Password=Secret123;", json, StringComparison.Ordinal);
        Assert.Contains("Password=***;", json, StringComparison.Ordinal);

        // Confirm the error class is INTERNAL.
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("INTERNAL", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("InvalidOperationException", doc.RootElement.GetProperty("exception_type").GetString());
    }

    private static SqlGuard CreateGuard(AccessMode mode = AccessMode.Restricted) => new(
        new MssqlMcpOptions
        {
            ConnectionString = "Server=localhost;",
            AccessMode = mode,
            QueryTimeout = 30,
            LogLevel = "info",
            MaxResultBytes = 10 * 1024 * 1024,
            RetryCount = 3,
            RetryIntervalMin = 2,
            RetryIntervalMax = 10,
        },
        NullLogger<SqlGuard>.Instance);

    private static MssqlMcpOptions RestrictedOptions() => new()
    {
        ConnectionString = "Server=localhost;",
        AccessMode = AccessMode.Restricted,
        QueryTimeout = 30,
        LogLevel = "info",
        MaxResultBytes = 10 * 1024 * 1024,
        RetryCount = 3,
        RetryIntervalMax = 10,
        RetryIntervalMin = 2,
    };
}
