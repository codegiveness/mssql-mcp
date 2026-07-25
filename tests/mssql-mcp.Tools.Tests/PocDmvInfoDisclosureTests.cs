using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using NSubstitute;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// PoC #5 — DMV info disclosure via get_top_queries (INVERTED after the fix).
/// ValidateDatabaseSql now includes `AND HAS_DBACCESS(name) = 1`, so any DB the
/// login lacks access to fails validation and the DMV query is never issued.
///
/// DC1 remains the static proof that the SQL template is server-scoped — still
/// valid and still documented here for context.
///
/// Written by poc-engineer-a for the security-research team run.
/// </summary>
public class PocDmvInfoDisclosureTests
{
    /// <summary>
    /// DC1 PROOF: TopQueriesSqlTemplate queries server-scoped DMVs. It does NOT use a
    /// database prefix ({0} placeholder is absent — contrast with MissingIndexWorkloadSqlTemplate
    /// at line 52 which uses {0}sys.dm_db_missing_index_*). The only DB scoping is
    /// WHERE st.dbid = {0}, where {0} is interpolated as DB_ID(@database).
    ///
    /// Still valid after the fix — the template structure is unchanged. The fix
    /// gates access upstream in ValidateDatabaseAsync via HAS_DBACCESS.
    /// </summary>
    [Fact]
    public void DC1_TopQueriesSqlTemplate_IsServerScoped_UsesDbIdFilter()
    {
        System.Reflection.FieldInfo? field = typeof(OpsTools).GetField(
            "TopQueriesSqlTemplate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        string sql = (string)field!.GetValue(null)!;

        // Server-scoped DMVs — no {0} database prefix.
        Assert.Contains("sys.dm_exec_query_stats", sql, StringComparison.Ordinal);
        Assert.Contains("sys.dm_exec_sql_text", sql, StringComparison.Ordinal);

        // The ONLY database filter is st.dbid = {0} (interpolated as DB_ID(@database)).
        Assert.Contains("st.dbid = {0}", sql, StringComparison.Ordinal);

        // Contrast: the per-DB templates use {0} prefix. This one does not.
        Assert.DoesNotContain("{0}sys.dm_exec_query_stats", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// DC2 PROOF (inverted): With HAS_DBACCESS in the validation SQL, an
    /// inaccessible DB returns 0 rows from sys.databases, validation fails, and
    /// get_top_queries returns the "does not exist" error. The DMV is never queried,
    /// so no sensitive query_text is disclosed.
    /// </summary>
    [Fact]
    public async Task DC2_GetTopQueries_RejectsDbWithNoLoginAccess()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();

        // ValidateDatabaseAsync returns 0 rows (HAS_DBACCESS=0 → inaccessible).
        executor.ExecuteQueryAsync(
                Arg.Is<string>(sql => sql != null && sql.Contains("sys.databases", StringComparison.Ordinal)),
                Arg.Any<IReadOnlyDictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        MssqlMcpOptions opts = RestrictedOptions();
        OpsTools tools = new(executor, Options.Create(opts), NullLogger<OpsTools>.Instance);

        // Agent requests top queries for `payroll` — a DB it should not have insight into.
        CallToolResult result = await tools.GetTopQueries("payroll", null, 10, CancellationToken.None);

        // Validation fails — tool returns an error.
        Assert.True(result.IsError ?? false);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        string json = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        Assert.Contains("does not exist", json, StringComparison.OrdinalIgnoreCase);

        // Sensitive data NOT in output.
        Assert.DoesNotContain("payroll.salaries", json, StringComparison.Ordinal);
        Assert.DoesNotContain("123-45-6789", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// DC3 PROOF (inverted): When validation fails (HAS_DBACCESS=0), the DMV query
    /// is never issued and no query_text is returned. No data leak.
    /// </summary>
    [Fact]
    public async Task DC3_GetTopQueries_NoDataLeak_WhenValidationFails()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();

        // ValidateDatabaseAsync returns 0 rows (HAS_DBACCESS=0 → inaccessible).
        executor.ExecuteQueryAsync(
                Arg.Is<string>(sql => sql != null && sql.Contains("sys.databases", StringComparison.Ordinal)),
                Arg.Any<IReadOnlyDictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        MssqlMcpOptions opts = RestrictedOptions();
        OpsTools tools = new(executor, Options.Create(opts), NullLogger<OpsTools>.Instance);

        CallToolResult result = await tools.GetTopQueries("targetdb", null, 10, CancellationToken.None);

        // Validation fails — tool returns an error.
        Assert.True(result.IsError ?? false);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Count >= 1);
        string json = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        // No sensitive literal anywhere in the output.
        Assert.DoesNotContain("4111111111111111", json, StringComparison.Ordinal);
        Assert.DoesNotContain("credit_card", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// DC4 PROOF (inverted): When the agent passes a database it lacks access to,
    /// validation fails (HAS_DBACCESS=0 → 0 rows) and the tool returns an error.
    /// Case A (no database param) still works — no validation needed when database is null.
    /// </summary>
    [Fact]
    public async Task DC4_GetTopQueries_ValidationFails_ForUnauthorizedDb()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();

        // Case A: no database param — uses DB_ID() (current DB), no validation call.
        // The DMV query returns empty.
        executor.ExecuteQueryAsync(
                Arg.Is<string>(sql => sql != null && sql.Contains("dm_exec_query_stats", StringComparison.Ordinal)),
                Arg.Any<IReadOnlyDictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        // Case B: agent passes "secret_db" — validation fails (HAS_DBACCESS=0 → 0 rows).
        executor.ExecuteQueryAsync(
                Arg.Is<string>(sql => sql != null && sql.Contains("sys.databases", StringComparison.Ordinal)),
                Arg.Any<IReadOnlyDictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        MssqlMcpOptions opts = RestrictedOptions();
        OpsTools tools = new(executor, Options.Create(opts), NullLogger<OpsTools>.Instance);

        // Case A: no database param — succeeds (no validation needed).
        CallToolResult resultA = await tools.GetTopQueries(null, null, 10, CancellationToken.None);
        Assert.False(resultA.IsError ?? false);

        // Case B: agent passes "secret_db" — validation fails.
        CallToolResult resultB = await tools.GetTopQueries("secret_db", null, 10, CancellationToken.None);
        Assert.True(resultB.IsError ?? false);
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
