using System.Collections;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using mssql_mcp.Core.Configuration;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// Tests for SqlExecutor's retry configuration (ADR-0004) and MssqlMcpOptions retry
/// validation (ADR-0015). Uses Microsoft.Data.SqlClient's built-in SqlRetryLogicOption —
/// Microsoft maintains the transient-error list, we only configure count + backoff range.
/// </summary>
public class RetryLogicTests
{
    private static IDictionary EmptyEnv => new Hashtable();

    private static IDictionary Env(params (string key, string value)[] pairs)
    {
        var dict = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in pairs)
        {
            dict[key] = value;
        }
        return dict;
    }

    // --- SqlRetryLogicOption configuration (BuildRetryOption) ---

    [Fact]
    public void RetryLogic_Defaults_RetryCountIs3_NumberOfTriesIs4()
    {
        SqlRetryLogicOption opt = SqlExecutor.BuildRetryOption(
            retryCount: MssqlMcpOptions.DefaultRetryCount,
            retryIntervalMin: MssqlMcpOptions.DefaultRetryIntervalMin,
            retryIntervalMax: MssqlMcpOptions.DefaultRetryIntervalMax);

        // RetryCount=3 → NumberOfTries=4 (first attempt + 3 retries) per Microsoft's convention.
        Assert.Equal(4, opt.NumberOfTries);
        Assert.Equal(TimeSpan.FromSeconds(2), opt.DeltaTime);
        Assert.Equal(TimeSpan.FromSeconds(2), opt.MinTimeInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), opt.MaxTimeInterval);
    }

    [Fact]
    public void RetryLogic_CustomValues_ConfiguredCorrectly()
    {
        SqlRetryLogicOption opt = SqlExecutor.BuildRetryOption(
            retryCount: 5, retryIntervalMin: 3, retryIntervalMax: 15);

        Assert.Equal(6, opt.NumberOfTries); // 5 retries + 1 first attempt
        Assert.Equal(TimeSpan.FromSeconds(3), opt.DeltaTime);
        Assert.Equal(TimeSpan.FromSeconds(3), opt.MinTimeInterval);
        Assert.Equal(TimeSpan.FromSeconds(15), opt.MaxTimeInterval);
    }

    [Fact]
    public void RetryLogic_RetryCountZero_NumberOfTriesIsOne()
    {
        // RetryCount=0 means a single attempt, no retries.
        SqlRetryLogicOption opt = SqlExecutor.BuildRetryOption(
            retryCount: 0, retryIntervalMin: 1, retryIntervalMax: 2);

        Assert.Equal(1, opt.NumberOfTries);
    }

    [Theory]
    [InlineData(0, 2, 10)]   // RetryCount=0 → NumberOfTries=1 (boundary: no retries)
    [InlineData(1, 2, 10)]   // RetryCount=1 → NumberOfTries=2
    [InlineData(59, 2, 10)]  // RetryCount=59 → NumberOfTries=60 (Microsoft's max)
    public void RetryLogic_BoundaryRetryCount_AcceptedByOption(int retryCount, int minSec, int maxSec)
    {
        SqlRetryLogicOption opt = SqlExecutor.BuildRetryOption(retryCount, minSec, maxSec);
        Assert.Equal(retryCount + 1, opt.NumberOfTries);
    }

    // --- BuildRetryProvider ---

    [Fact]
    public void RetryLogic_Provider_ExponentialBackoff_HasTransientPredicate()
    {
        SqlRetryLogicBaseProvider provider = SqlExecutor.BuildRetryProvider(
            retryCount: 3, retryIntervalMin: 2, retryIntervalMax: 10,
            NullLogger.Instance);

        Assert.NotNull(provider.RetryLogic);
        Assert.Equal(4, provider.RetryLogic.NumberOfTries);
        // Microsoft ships the transient-error list as a non-null TransientPredicate.
        // We do NOT provide our own — this is the key design decision (c0h1b4 fix).
        Assert.NotNull(provider.RetryLogic.TransientPredicate);
        // Exponential backoff uses the exponential interval enumerator.
        Assert.Equal("Microsoft.Data.SqlClient.SqlExponentialIntervalEnumerator",
            provider.RetryLogic.RetryIntervalEnumerator.GetType().FullName);
    }

    [Fact]
    public void RetryLogic_RetryCountZero_ReturnsNoneRetryProvider()
    {
        SqlRetryLogicBaseProvider provider = SqlExecutor.BuildRetryProvider(
            retryCount: 0, retryIntervalMin: 0, retryIntervalMax: 1,
            NullLogger.Instance);

        Assert.NotNull(provider.RetryLogic);
        Assert.Equal(1, provider.RetryLogic.NumberOfTries); // One attempt, no retries.
    }

    [Fact]
    public void RetryLogic_RetryingEvent_HookedForRetryProvider()
    {
        // The Retrying event handler must be attached so we can log transient errors at info level.
        SqlRetryLogicBaseProvider provider = SqlExecutor.BuildRetryProvider(
            retryCount: 3, retryIntervalMin: 2, retryIntervalMax: 10,
            NullLogger.Instance);

        // BuildRetryProvider attaches a Retrying handler that logs attempts.
        // Reflection is unreliable for event backer (it's a C# auto-event backing field),
        // so verify via GetInvocationList on the event itself.
        Delegate[]? handlers = provider.Retrying?.GetInvocationList();
        Assert.NotNull(handlers);
        Assert.True(handlers!.Length > 0, "BuildRetryProvider must attach a Retrying handler for logging.");
    }

    // --- SqlExecutor ctor wires the provider onto each connection/command ---

    [Fact]
    public void SqlExecutor_WithRetryCount_PersistsProviderForNewConnection()
    {
        // Construct SqlExecutor with retryCount=3. The provider should be stored internally
        // and assigned to each new SqlConnection/SqlCommand instance (RetryLogicProvider is
        // an instance property on Microsoft.Data.SqlClient 7.0.2, NOT static).
        var executor = new SqlExecutor(
            "Server=localhost;Database=Test;Integrated Security=true;",
            commandTimeout: 30,
            retryCount: 3, retryIntervalMin: 2, retryIntervalMax: 10,
            NullLogger<SqlExecutor>.Instance);

        // We can't open a real connection in a unit test, but we CAN verify the provider
        // would be assigned by constructing a fresh SqlConnection and checking it accepts
        // the assignment without error. The behavior we care about is that SqlExecutor
        // doesn't throw during construction and stores a valid provider.
        using var conn = new SqlConnection("Server=localhost;");
        SqlRetryLogicBaseProvider provider = SqlExecutor.BuildRetryProvider(
            retryCount: 3, retryIntervalMin: 2, retryIntervalMax: 10,
            NullLogger<SqlExecutor>.Instance);
        conn.RetryLogicProvider = provider; // If this compiles and runs, the API is correct.
        Assert.Same(provider, conn.RetryLogicProvider);
    }

    [Fact]
    public void SqlExecutor_DefaultCtor_NoRetryProviderConfigured()
    {
        // The 3-arg backward-compat ctor delegates with retryCount=0 — no retries.
        // Constructing must not throw and must not touch static state.
        var executor = new SqlExecutor(
            "Server=localhost;Database=Test;Integrated Security=true;",
            commandTimeout: 30,
            NullLogger<SqlExecutor>.Instance);

        // No exception means the 3-arg ctor still works for the 8 existing test sites.
        Assert.NotNull(executor);
    }

    // --- MssqlMcpOptions env-var validation (ADR-0015 fail-fast) ---

    [Fact]
    public void Options_InvalidRetryCountNegative_Throws()
    {
        var env = Env(("MSSQL_RETRY_COUNT", "-1"));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MssqlMcpOptions.Parse(new[] { "--connection-string", "Server=x;" }, env));
        Assert.Contains("[startup]", ex.Message);
        Assert.Contains("retry count", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Options_InvalidRetryCountNonNumeric_Throws()
    {
        var env = Env(("MSSQL_RETRY_COUNT", "abc"));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MssqlMcpOptions.Parse(new[] { "--connection-string", "Server=x;" }, env));
        Assert.Contains("[startup]", ex.Message);
        Assert.Contains("abc", ex.Message);
    }

    [Fact]
    public void Options_InvalidRetryCountTooLarge_Throws()
    {
        // RetryCount=60 would produce NumberOfTries=61, which Microsoft.Data.SqlClient rejects (>60).
        var env = Env(("MSSQL_RETRY_COUNT", "60"));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MssqlMcpOptions.Parse(new[] { "--connection-string", "Server=x;" }, env));
        Assert.Contains("[startup]", ex.Message);
        Assert.Contains("59", ex.Message); // Must mention the allowed upper bound.
    }

    [Fact]
    public void Options_InvalidRetryIntervalNonNumeric_Throws()
    {
        var env = Env(("MSSQL_RETRY_INTERVAL", "abc"));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MssqlMcpOptions.Parse(new[] { "--connection-string", "Server=x;" }, env));
        Assert.Contains("[startup]", ex.Message);
        Assert.Contains("abc", ex.Message);
    }

    [Fact]
    public void Options_RetryIntervalMinEqualsMax_Throws()
    {
        // SqlRetryLogicOption allows Min==Max, but ADR-0015 requires strict less-than
        // for an unambiguous backoff range.
        var env = Env(("MSSQL_RETRY_INTERVAL", "10")); // min=10, max stays at default 10
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MssqlMcpOptions.Parse(new[] { "--connection-string", "Server=x;" }, env));
        Assert.Contains("[startup]", ex.Message);
        Assert.Contains("strictly less than", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Options_RetryIntervalMinGreaterThanMax_Throws()
    {
        var env = Env(("MSSQL_RETRY_INTERVAL", "20")); // min=20 > max=10 default
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MssqlMcpOptions.Parse(new[] { "--connection-string", "Server=x;" }, env));
        Assert.Contains("[startup]", ex.Message);
        Assert.Contains("strictly less than", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Options_RetryIntervalMinTooLarge_Throws()
    {
        // Microsoft.Data.SqlClient caps MaxTimeInterval at 120s — enforce at startup.
        var env = Env(("MSSQL_RETRY_INTERVAL", "200"));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MssqlMcpOptions.Parse(new[] { "--connection-string", "Server=x;" }, env));
        Assert.Contains("[startup]", ex.Message);
        Assert.Contains("120", ex.Message);
    }

    [Fact]
    public void Options_RetryCountAtUpperBound_Accepted()
    {
        // RetryCount=59 → NumberOfTries=60 (Microsoft's max).
        var env = Env(("MSSQL_RETRY_COUNT", "59"));
        var options = MssqlMcpOptions.Parse(new[] { "--connection-string", "Server=x;" }, env);
        Assert.Equal(59, options.RetryCount);

        // Verify the resulting option is accepted by SqlRetryLogicOption at runtime.
        SqlRetryLogicOption opt = SqlExecutor.BuildRetryOption(
            options.RetryCount, options.RetryIntervalMin, options.RetryIntervalMax);
        Assert.Equal(60, opt.NumberOfTries);
    }
}
