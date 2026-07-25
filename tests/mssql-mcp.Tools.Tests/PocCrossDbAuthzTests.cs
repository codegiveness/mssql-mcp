using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;
using NSubstitute;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// PoC #4 — Cross-DB authz gap (INVERTED after the fix). The ValidateDatabaseSql
/// template now includes `AND HAS_DBACCESS(name) = 1`, so any database the SQL
/// Server login lacks access to returns 0 rows and surfaces the same "does not
/// exist" error used for genuinely non-existent DBs — no access-info leak.
///
/// Written by poc-engineer-a for the security-research team run.
/// </summary>
public class PocCrossDbAuthzTests
{
    /// <summary>
    /// CB1 PROOF (inverted): ValidateDatabaseSql (private const on SqlHelpers) now
    /// CONTAINS the HAS_DBACCESS permission predicate. The three existence/state
    /// checks are still present, and no other permission primitives are needed.
    /// </summary>
    [Fact]
    public void CB1_ValidateDatabaseSql_HasPermissionPredicate()
    {
        System.Reflection.FieldInfo? field = typeof(SqlHelpers).GetField(
            "ValidateDatabaseSql",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        string sql = (string)field!.GetValue(null)!;

        // The three existence/state checks ARE present.
        Assert.Contains("state_desc", sql, StringComparison.Ordinal);
        Assert.Contains("user_access_desc", sql, StringComparison.Ordinal);

        // The HAS_DBACCESS permission predicate is present.
        Assert.Contains("HAS_DBACCESS", sql, StringComparison.OrdinalIgnoreCase);

        // No other permission primitives are in scope.
        Assert.DoesNotContain("HAS_PERMS_BY_NAME", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fn_my_permissions", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("permission", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// CB2 PROOF (inverted): When HAS_DBACCESS returns 0/NULL for an inaccessible
    /// database, sys.databases returns 0 rows and ValidateDatabaseAsync fails with
    /// the "does not exist" error — the login cannot tell whether the DB exists.
    /// </summary>
    [Fact]
    public async Task CB2_ValidateDatabaseAsync_RejectsDbWithNoLoginAccess()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // HAS_DBACCESS returns 0/NULL → 0 rows. The login has no access to `payroll`.
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        DatabaseValidationResult result =
            await SqlHelpers.ValidateDatabaseAsync(executor, "payroll", CancellationToken.None);

        // Validation fails — the server would NOT proceed to [payroll].sys.* queries.
        Assert.False(result.Valid, "payroll must be rejected — HAS_DBACCESS returned 0/NULL");
        Assert.NotNull(result.Error);
    }

    /// <summary>
    /// CB3 PROOF (inverted): When validation fails (HAS_DBACCESS=0 → 0 rows),
    /// ListObjects returns an error and never issues the cross-DB sys.objects query.
    /// </summary>
    [Fact]
    public async Task CB3_ListObjects_NoCrossDbQuery_WhenValidationFails()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();

        // First call: ValidateDatabaseAsync (sys.databases) → 0 rows (HAS_DBACCESS=0).
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        MssqlMcpOptions opts = RestrictedOptions();
        DatabaseTools tools = new(executor, Options.Create(opts), NullLogger<DatabaseTools>.Instance);

        // Agent (or attacker controlling the agent) requests objects from `payroll`.
        CallToolResult result = await tools.ListObjects("payroll", null, null, 100, CancellationToken.None);

        // The tool surfaces an error — the "does not exist" message.
        Assert.True(result.IsError ?? false);

        // The cross-DB sys.objects query was NEVER issued.
        await executor.DidNotReceive().ExecuteQueryAsync(
            Arg.Is<string>(sql => sql != null && sql.Contains("[payroll].sys.objects", StringComparison.Ordinal)),
            Arg.Any<IReadOnlyDictionary<string, object>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// CB4 INFO-LEAK (inverted): When the login lacks access, validation fails FIRST
    /// and the tool returns the generic "does not exist" error — the DB name is not
    /// surfaced and the agent cannot distinguish non-existent from inaccessible.
    /// </summary>
    [Fact]
    public async Task CB4_CrossDbAccessDenied_ReturnsDoesNotExistError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();

        // ValidateDatabaseAsync returns 0 rows (HAS_DBACCESS=0 → inaccessible).
        executor.ExecuteQueryAsync(
                Arg.Is<string>(sql => sql != null && sql.Contains("sys.databases", StringComparison.Ordinal)),
                Arg.Any<IReadOnlyDictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        MssqlMcpOptions opts = RestrictedOptions();
        DatabaseTools tools = new(executor, Options.Create(opts), NullLogger<DatabaseTools>.Instance);

        // Agent requests list_objects on `payroll` it cannot access.
        CallToolResult result = await tools.ListObjects("payroll", null, null, 100, CancellationToken.None);

        // The error surfaces — but it's the generic "does not exist" message.
        Assert.True(result.IsError ?? false);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        string json = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        Assert.Contains("does not exist", json, StringComparison.OrdinalIgnoreCase);
        // No permission-denied language — the agent cannot distinguish inaccessible
        // from non-existent. (The DB name is the agent's own input, not a leak.)
        Assert.DoesNotContain("permission", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("denied", json, StringComparison.OrdinalIgnoreCase);
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
