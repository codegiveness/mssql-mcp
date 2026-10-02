using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// Integration test for the Restricted-mode transaction wrapper per ADR-0007.
/// Verifies that an INSERT wrapped in BEGIN TRANSACTION ... ROLLBACK TRANSACTION
/// rolls back against a real SQL Server. Tagged Category=Integration — skipped in CI.
/// Requires MSSQL_CONNECTION_STRING env var pointing at a real SQL Server.
/// </summary>
[Trait("Category", "Integration")]
public class GuardIntegrationTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING");

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(mssql_mcp.Tests.IntegrationEnvironment.Enabled), SkipType = typeof(mssql_mcp.Tests.IntegrationEnvironment))]
    public async Task TransactionWrapper_RollsBackInsert_RowCountStaysZero()
    {


        // Use a unique temp table name per run to avoid collisions.
        string tableName = $"mssql_mcp_guard_test_{Guid.NewGuid():N}";

        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // Create a scratch table outside the transaction wrapper.
        using (SqlCommand create = new(
            $"CREATE TABLE [{tableName}] (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(50) NOT NULL);",
            connection))
        {
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        try
        {
            // Simulate the Guard's wrapped SQL for an INSERT. In production the Guard's
            // AST allowlist would reject INSERT; this test exercises the transaction rollback
            // backstop directly — if the AST allowlist ever misses a destructive statement,
            // the rollback must prevent the commit.
            string wrappedSql =
                $"""
                /* mssql-mcp */
                BEGIN TRANSACTION
                INSERT INTO [{tableName}] (Id, Name) VALUES (1, 'test');
                ROLLBACK TRANSACTION
                """;

            using (SqlCommand insert = new(wrappedSql, connection))
            {
                await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            // The INSERT must have rolled back — verify rowcount is 0.
            using (SqlCommand count = new($"SELECT COUNT(*) FROM [{tableName}];", connection))
            {
                int rowCount = Assert.IsType<int>(await count.ExecuteScalarAsync(TestContext.Current.CancellationToken));
                Assert.Equal(0, rowCount);
            }
        }
        finally
        {
            // Cleanup the scratch table.
            using SqlCommand drop = new($"DROP TABLE IF EXISTS [{tableName}];", connection);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact(Skip = "Requires INTEGRATION=true and MSSQL_CONNECTION_STRING.", SkipUnless = nameof(mssql_mcp.Tests.IntegrationEnvironment.Enabled), SkipType = typeof(mssql_mcp.Tests.IntegrationEnvironment))]
    public async Task Guard_AcceptsSelectAgainstRealServer_ReturnsRows()
    {


        string connStr = ConnectionString ?? throw new InvalidOperationException("MSSQL_CONNECTION_STRING must be set");

        MssqlMcpOptions options = new()
        {
            ConnectionString = connStr,
            AccessMode = AccessMode.Restricted,
            QueryTimeout = 30,
            LogLevel = "info",
            MaxResultBytes = 10 * 1024 * 1024,
            RetryCount = 3,
            RetryIntervalMin = 2,
            RetryIntervalMax = 10,
        };
        SqlGuard guard = new(options, NullLogger<SqlGuard>.Instance);
        GuardResult result = guard.Validate("SELECT 1 AS Value");
        Assert.True(result.Accepted, $"Expected accept, got rejection: {result.Rejection?.Rule}");
        Assert.NotNull(result.WrappedSql);

        // Execute the wrapped SQL and verify a single row comes back.
        SqlExecutor executor = new(options.ConnectionString, options.QueryTimeout,
            options.RetryCount, options.RetryIntervalMin, options.RetryIntervalMax,
            NullLogger<SqlExecutor>.Instance);
        List<Dictionary<string, object?>> rows =
            (await executor.ExecuteQueryAsync(result.WrappedSql, 0, CancellationToken.None)).Rows;
        Assert.Single(rows);
        Assert.Equal(1, Assert.IsType<int>(rows[0]["Value"]));
    }
}
