using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using mssql_mcp.Tests;

namespace mssql_mcp.Core.Tests;

[Trait("Category", "Integration")]
public class SqlExecutorIntegrationTests
{
    private static SqlExecutor CreateExecutor()
    {
        SqlConnectionStringBuilder connection = new(Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING"))
        {
            ApplicationName = "mssql-mcp-lifetime-" + Guid.NewGuid().ToString("N"),
            MaxPoolSize = 1,
        };
        return new SqlExecutor(connection.ConnectionString, 0, 0, 2, 10, NullLogger<SqlExecutor>.Instance);
    }

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(IntegrationEnvironment.Enabled), SkipType = typeof(IntegrationEnvironment))]
    public async Task CoercionAcrossRows_PreservesIndependentRowsNullsAndFullDecimalPrecision()
    {
        SqlExecutor executor = CreateExecutor();
        SqlQueryResult result = await executor.ExecuteQueryAsync("""
            SELECT n AS v, text_value AS text_value,
                   CAST('12345678901234567890123456789012345678' AS decimal(38,0)) AS precise
            FROM (VALUES (1, N'first'), (2, NULL), (3, N'last')) values_table(n, text_value)
            ORDER BY n
            """, 0, TestContext.Current.CancellationToken);

        Assert.False(result.IsTruncated);
        Assert.Collection(result.Rows,
            row => { Assert.Equal(1, row["v"]); Assert.Equal("first", row["text_value"]); },
            row => { Assert.Equal(2, row["v"]); Assert.Null(row["text_value"]); },
            row => { Assert.Equal(3, row["v"]); Assert.Equal("last", row["text_value"]); });
        Assert.All(result.Rows, row => Assert.Equal("12345678901234567890123456789012345678", row["precise"]));
    }

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(IntegrationEnvironment.Enabled), SkipType = typeof(IntegrationEnvironment))]
    public async Task CancelledBlockedPlan_WithUnlimitedQueryTimeout_LeavesSmallPoolUsable()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SqlExecutor executor = CreateExecutor();
        await executor.ExecuteQueryAsync("SELECT 1 AS warmup", 0, ct);

        // A physical close must end the creator session even when the assertion fails.
        SqlConnectionStringBuilder blockerConnection = new(Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING"))
        {
            Pooling = false,
        };
        await using SqlConnection blocker = new(blockerConnection.ConnectionString);
        await blocker.OpenAsync(ct);
        string table = "##mssql_mcp_plan_" + Guid.NewGuid().ToString("N");
        await using (SqlCommand create = new($"CREATE TABLE [{table}] (Id int);", blocker))
        {
            await create.ExecuteNonQueryAsync(ct);
        }

        await using SqlTransaction transaction = (SqlTransaction)await blocker.BeginTransactionAsync(ct);
        await using (SqlCommand alter = new($"ALTER TABLE [{table}] ADD Value int NULL;", blocker, transaction))
        {
            await alter.ExecuteNonQueryAsync(ct);
        }

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancellation.CancelAfter(TimeSpan.FromSeconds(1));
        Task<string> plan = executor.ExecuteShowPlanXmlAsync($"SELECT * FROM [{table}]", 0, cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plan.WaitAsync(TimeSpan.FromSeconds(15), ct));

        SqlQueryResult next = await executor.ExecuteQueryAsync("SELECT 42 AS answer", 0, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.False(next.IsTruncated);
        Assert.Equal(42, Assert.Single(next.Rows)["answer"]);
        await transaction.RollbackAsync(ct);
        // The creator connection owns the global temporary table; disposal removes it.
    }
}
