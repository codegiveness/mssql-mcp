using Microsoft.Extensions.Logging.Abstractions;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// Unit tests for the Restricted-mode Guard AST validation per ADR-0006.
/// All attack vectors from ticket 02 are covered: statement-type allowlist (Layer 1),
/// intra-SELECT blocklist (Layer 2), multi-statement, GO-separated, and nested statements.
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

    /// <summary>
    /// TSqlStatementSnippet is produced on partial-parse fallback. Empirical probing against
    /// ScriptDom 180.37.3 could not trigger a TSqlStatementSnippet via the public TSql160Parser.Parse
    /// entry point — every malformed input we tried either produced a parse error (rule=parse_error)
    /// or a fully-typed statement. The branch remains in the implementation as defense-in-depth for
    /// future ScriptDom versions; this test documents that no reproducible snippet input exists today.
    /// </summary>
    [Fact(Skip = "TSqlStatementSnippet cannot be triggered via TSql160Parser.Parse with any known malformed input in ScriptDom 180.37.3 — see comment for details.")]
    public void Reject_StatementSnippet_CannotBeTriggeredDirectly()
    {
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
