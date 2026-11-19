using Microsoft.Extensions.Logging.Abstractions;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// Unit tests for the Restricted-mode Guard AST validation per ADR-0006.
/// Covers the full ADR-0006 attack vector matrix (32 vectors across 5 categories):
/// Layer 1 statement-type allowlist (10), Layer 1 edge cases (4),
/// Layer 2 intra-SELECT blocklist (11), ADR-0006 Consequences implied (2),
/// and SqlGuard.cs implementation paths (5).
/// Closes ADR-0014 §5 qualitative + quantitative (≥30) gate — see issue #13.
/// </summary>
public class GuardTests
{
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

    /// <summary>
    /// Asserts the result is a rejection and returns the non-null <see cref="GuardRejection"/>
    /// for further assertions, without using the null-forgiving operator.
    /// </summary>
    private static GuardRejection RequireRejection(GuardResult result)
    {
        Assert.False(result.Accepted, $"Expected rejection but got accept with SQL: {result.WrappedSql}");
        Assert.NotNull(result.Rejection);
        return result.Rejection;
    }

    // ---------- Accept cases ----------

    [Fact]
    public void Accept_SimpleSelect()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT 1");
        Assert.True(result.Accepted, $"Expected accept, got rejection: {result.Rejection?.Rule} — {result.Rejection?.Detail}");
        Assert.NotNull(result.WrappedSql);
    }

    [Fact]
    public void Accept_WithCte_Select()
    {
        var guard = CreateGuard();
        var result = guard.Validate("WITH cte AS (SELECT 1) SELECT * FROM cte");
        Assert.True(result.Accepted, $"Expected accept, got rejection: {result.Rejection?.Rule} — {result.Rejection?.Detail}");
    }

    [Fact]
    public void Accept_SimpleSelectWithWhere()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT * FROM Users WHERE name = 'test'");
        Assert.True(result.Accepted);
    }

    [Fact]
    public void Accept_SystemCatalogView()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT * FROM sys.databases");
        Assert.True(result.Accepted);
    }

    // ---------- Reject: Layer 1 statement-type ----------

    [Fact]
    public void Reject_MultiStatementInOneBatch()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT 1; DROP TABLE x");
        var rejection = RequireRejection(result);
        Assert.Equal("non_select_statement", rejection.Rule);
        Assert.NotNull(rejection.StatementType);
        Assert.Contains("DropTableStatement", rejection.StatementType, StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_GoSeparatedBatches_SingleLine()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT 1 GO DROP TABLE x");
        Assert.Equal("non_select_statement", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_GoSeparatedBatches_MultiLine()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT 1\nGO\nDROP TABLE x");
        Assert.Equal("non_select_statement", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_NestedInBeginEnd()
    {
        var guard = CreateGuard();
        var result = guard.Validate("BEGIN DROP TABLE x END");
        Assert.Equal("non_select_statement", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_NestedInIf()
    {
        var guard = CreateGuard();
        var result = guard.Validate("IF (1=1) DROP TABLE x");
        Assert.Equal("non_select_statement", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_NestedInWhile()
    {
        var guard = CreateGuard();
        var result = guard.Validate("WHILE (1=1) DROP TABLE x");
        Assert.Equal("non_select_statement", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_CteWithDelete()
    {
        var guard = CreateGuard();
        var result = guard.Validate("WITH cte AS (SELECT 1) DELETE FROM cte");
        var rejection = RequireRejection(result);
        Assert.Equal("non_select_statement", rejection.Rule);
        Assert.Contains("DeleteStatement", rejection.StatementType ?? "", StringComparison.Ordinal);
    }

    // ---------- Reject: Layer 2 intra-SELECT ----------

    [Fact]
    public void Reject_SelectInto()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT * INTO #temp FROM Users");
        Assert.Equal("select_into", RequireRejection(result).Rule);
    }

    [Theory]
    [InlineData("SELECT NEXT VALUE FOR dbo.ReviewSequence")]
    [InlineData("SELECT NEXT VALUE FOR dbo.ReviewSequence FROM (VALUES (1), (2)) AS v(Id)")]
    [InlineData("SELECT NEXT VALUE FOR dbo.ReviewSequence OVER (ORDER BY v.Id) FROM (VALUES (1), (2)) AS v(Id)")]
    public void SequenceAllocation_IsRejectedByRestrictedAndStrictValidation_ButAllowedUnrestricted(string sql)
    {
        var restricted = CreateGuard();
        var restrictedResult = restricted.Validate(sql);
        var restrictedRejection = RequireRejection(restrictedResult);
        Assert.Equal("next_value_for", restrictedRejection.Rule);
        Assert.Equal(1, restrictedRejection.Line);
        Assert.Equal(8, restrictedRejection.Column);
        Assert.Null(restrictedResult.WrappedSql);

        var unrestricted = CreateGuard(AccessMode.Unrestricted);
        var unrestrictedResult = unrestricted.Validate(sql);
        Assert.True(unrestrictedResult.Accepted);
        Assert.Equal(sql, unrestrictedResult.WrappedSql);
        Assert.Null(unrestrictedResult.Rejection);

        var strictResult = unrestricted.ValidateStrict(sql);
        Assert.Equal("next_value_for", RequireRejection(strictResult).Rule);
        Assert.Null(strictResult.WrappedSql);
    }

    [Fact]
    public void Accept_SequenceLikeText_IsNotSequenceAllocation()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT 'NEXT VALUE FOR dbo.ReviewSequence' AS Description");

        Assert.True(result.Accepted, $"Expected accept, got rejection: {result.Rejection?.Rule}");
        Assert.Null(result.Rejection);
    }

    [Fact]
    public void Reject_OpenRowset()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=local;Trusted_Connection=true;', 'SELECT 1')");
        Assert.Equal("openrowset", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_OpenRowsetBulk()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT * FROM OPENROWSET(BULK 'C:\\file.txt', SINGLE_CLOB) AS x");
        Assert.Equal("openrowset_bulk", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_OpenRowsetCosmos()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT * FROM OPENROWSET('cosmosdb', 'account=...;database=...', 'SELECT * FROM c')");
        Assert.True(result.Rejection is not null, "OPENROWSET Cosmos should be rejected");
    }

    [Fact]
    public void Reject_OpenQuery()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT * FROM OPENQUERY(link, 'SELECT 1')");
        Assert.Equal("openquery", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_OpenXml()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT * FROM OPENXML(@id, '/root', 1)");
        Assert.Equal("openxml", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_OpenDataSource()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT * FROM OPENDATASOURCE('SQLNCLI', 'Data Source=local;Integrated Security=SSPI;').Northwind.dbo.Employees");
        Assert.Equal("opendatasource", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_ExecuteAs()
    {
        var guard = CreateGuard();
        var result = guard.Validate("EXECUTE AS USER = 'sa'");
        Assert.Equal("execute_as", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_FourPartName()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT * FROM [server].[db].[dbo].[table]");
        Assert.Equal("four_part_name", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_BulkInsert()
    {
        var guard = CreateGuard();
        var result = guard.Validate("BULK INSERT target FROM 'file.csv'");
        Assert.Equal("bulk_insert", RequireRejection(result).Rule);
    }

    // ---------- Reject: edge cases ----------

    [Fact]
    public void Reject_CommentOnly_EmptyBatch()
    {
        var guard = CreateGuard();
        // A comment-only input produces 0 batches per ScriptDom — no statement exists.
        var result = guard.Validate("-- DROP TABLE x");
        Assert.Equal("empty_batch", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_EmptyString_EmptyBatch()
    {
        var guard = CreateGuard();
        var result = guard.Validate("");
        Assert.Equal("empty_batch", RequireRejection(result).Rule);
    }

    [Fact]
    public void Reject_ParseError_ReportsLineAndColumn()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT !!!");
        var rejection = RequireRejection(result);
        Assert.Equal("parse_error", rejection.Rule);
        Assert.NotNull(rejection.Line);
        Assert.NotNull(rejection.Column);
        Assert.True(rejection.Line >= 1, $"Line should be >= 1, got {rejection.Line}");
        Assert.True(rejection.Column >= 1, $"Column should be >= 1, got {rejection.Column}");
    }

    // ---------- Reject: ADR-0006 coverage gap closure (issue #13) ----------

    [Fact]
    public void Reject_CteWithInsert()
    {
        var guard = CreateGuard();
        var result = guard.Validate("WITH cte AS (SELECT 1) INSERT INTO t SELECT * FROM cte");
        var rejection = RequireRejection(result);
        Assert.Equal("non_select_statement", rejection.Rule);
        Assert.Contains("InsertStatement", rejection.StatementType ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_CteWithUpdate()
    {
        var guard = CreateGuard();
        var result = guard.Validate("WITH cte AS (SELECT 1) UPDATE t SET x = 1 FROM cte");
        var rejection = RequireRejection(result);
        Assert.Equal("non_select_statement", rejection.Rule);
        Assert.Contains("UpdateStatement", rejection.StatementType ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_CteWithMerge()
    {
        var guard = CreateGuard();
        var result = guard.Validate("WITH cte AS (SELECT 1) MERGE INTO t USING cte ON 1=1 WHEN MATCHED THEN DELETE;");
        var rejection = RequireRejection(result);
        Assert.Equal("non_select_statement", rejection.Rule);
        Assert.Contains("MergeStatement", rejection.StatementType ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_DeclareTableVariable()
    {
        var guard = CreateGuard();
        // ADR-0006 Consequences: DECLARE @t TABLE is not a SELECT and is rejected before
        // the following SELECT is visited (first-rejection-wins).
        var result = guard.Validate("DECLARE @t TABLE (id INT); SELECT * FROM @t");
        var rejection = RequireRejection(result);
        Assert.Equal("non_select_statement", rejection.Rule);
        Assert.Contains("DeclareTableVariableStatement", rejection.StatementType ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_ExecuteStoredProcedure()
    {
        var guard = CreateGuard();
        // ADR-0006 Consequences: EXECUTE of even read-only system SPs is not permitted.
        var result = guard.Validate("EXEC sp_help");
        var rejection = RequireRejection(result);
        Assert.Equal("non_select_statement", rejection.Rule);
        Assert.Contains("ExecuteStatement", rejection.StatementType ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_ExecuteAsNestedInBeginEnd()
    {
        var guard = CreateGuard();
        // EXECUTE AS nested inside BEGIN/END is rejected by the Layer 1 catch-all
        // (BeginEndBlockStatement fires non_select_statement before the inner
        // ExecuteAsStatement is visited — first-rejection-wins). The attack vector
        // is still blocked; the rule differs from Reject_ExecuteAs because the
        // outer statement is visited first.
        var result = guard.Validate("BEGIN EXECUTE AS USER = 'sa' END");
        var rejection = RequireRejection(result);
        Assert.Equal("non_select_statement", rejection.Rule);
        Assert.Contains("BeginEndBlockStatement", rejection.StatementType ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_BulkInsertNestedInIf()
    {
        var guard = CreateGuard();
        // BULK INSERT nested inside IF is rejected by the Layer 1 catch-all
        // (IfStatement fires non_select_statement before the inner BulkInsertStatement
        // is visited — first-rejection-wins). The attack vector is still blocked; the
        // rule differs from Reject_BulkInsert because the outer statement is visited first.
        var result = guard.Validate("IF (1=1) BULK INSERT t FROM 'file.csv'");
        var rejection = RequireRejection(result);
        Assert.Equal("non_select_statement", rejection.Rule);
        Assert.Contains("IfStatement", rejection.StatementType ?? "", StringComparison.Ordinal);
    }

    // ---------- Unrestricted mode ----------

    [Fact]
    public void UnrestrictedMode_SkipsValidation()
    {
        var guard = CreateGuard(AccessMode.Unrestricted);
        // In Unrestricted mode, even destructive SQL is accepted — the Guard does not gate.
        var result = guard.Validate("SELECT 1; DROP TABLE x");
        Assert.True(result.Accepted, $"Unrestricted mode should accept, got rejection: {result.Rejection?.Rule}");
    }

    // ---------- Wrapped SQL shape ----------

    [Fact]
    public void WrappedSql_ContainsSentinelComment()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT 1");
        Assert.True(result.Accepted);
        Assert.Contains("/* mssql-mcp */", result.WrappedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void WrappedSql_ContainsTransactionWrapper()
    {
        var guard = CreateGuard();
        var result = guard.Validate("SELECT 1");
        Assert.True(result.Accepted);
        Assert.Contains("BEGIN TRANSACTION", result.WrappedSql, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION", result.WrappedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void WrappedSql_ContainsOriginalSql()
    {
        var guard = CreateGuard();
        var sql = "SELECT 1";
        var result = guard.Validate(sql);
        Assert.True(result.Accepted);
        Assert.Contains(sql, result.WrappedSql, StringComparison.Ordinal);
    }
}
