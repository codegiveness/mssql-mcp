using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Integration tests for the --validate flag against a real SQL Server.
/// Tagged Category=Integration — skipped in CI via --filter Category!=Integration.
/// Requires MSSQL_CONNECTION_STRING env var pointing at a real SQL Server.
/// </summary>
[Trait("Category", "Integration")]
public class ValidateIntegrationTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING");

    private static MssqlMcpOptions ValidOptions() => new()
    {
        ConnectionString = ConnectionString!,
        AccessMode = AccessMode.Restricted,
        QueryTimeout = 30,
        LogLevel = "info",
        MaxResultBytes = 10 * 1024 * 1024,
        RetryCount = 3,
        RetryIntervalMin = 2,
        RetryIntervalMax = 10,
    };

    [Fact(Skip = "Integration test — set MSSQL_CONNECTION_STRING and run without the Category!=Integration filter.")]
    public async Task Validate_ValidConnection_ExitsZero()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        (bool ok, string message) = await ConnectionValidator.ValidateAsync(ValidOptions(), CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(ConnectionValidator.SuccessMessage, message);
    }

    // Does NOT require a real DB — bad host always fails at OpenAsync. Runs in CI.
    [Fact]
    public async Task Validate_InvalidConnection_ReturnsFalseWithObfuscatedPassword()
    {
        // Bad host: never resolves, fails at OpenAsync.
        MssqlMcpOptions options = new()
        {
            ConnectionString = "Server=nonexistent.invalid.host.example;Database=master;User Id=sa;Password=hunter2;Connect Timeout=1;Encrypt=False;TrustServerCertificate=True;",
            RetryCount = 0,
            RetryIntervalMin = 0,
            RetryIntervalMax = 1,
        };

        (bool ok, string message) = await ConnectionValidator.ValidateAsync(options, CancellationToken.None);

        Assert.False(ok);
        Assert.StartsWith(ConnectionValidator.FailurePrefix, message);
        Assert.DoesNotContain("hunter2", message);
    }
}
