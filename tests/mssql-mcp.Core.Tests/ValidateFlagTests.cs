using System.Collections;
using mssql_mcp.Core.Configuration;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// Tests for the --validate CLI flag (ticket 10): option parsing and ConnectionValidator
/// behavior. Real-DB integration tests live in mssql-mcp.Tools.Tests/ValidateIntegrationTests.cs.
/// </summary>
public class ValidateFlagTests
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

    // --- --validate flag parsing ---

    [Fact]
    public void Validate_Flag_Parsed_True()
    {
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;", "--validate" },
            EmptyEnv);
        Assert.True(options.Validate);
    }

    [Fact]
    public void Validate_Flag_NotPresent_DefaultFalse()
    {
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;" },
            EmptyEnv);
        Assert.False(options.Validate);
    }

    [Fact]
    public void Validate_Flag_EqualsTrue_Form_Parsed_True()
    {
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;", "--validate=true" },
            EmptyEnv);
        Assert.True(options.Validate);
    }

    [Fact]
    public void Validate_Flag_EqualsFalse_Form_Parsed_False()
    {
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;", "--validate=false" },
            EmptyEnv);
        Assert.False(options.Validate);
    }

    [Fact]
    public void Validate_Flag_DoesNotRequireConnectionStringValue_NextToken()
    {
        // --validate is a boolean switch, not "--flag value". The token after --validate
        // (here --connection-string) must not be consumed as the flag's value.
        var options = MssqlMcpOptions.Parse(
            new[] { "--validate", "--connection-string", "Server=x;" },
            EmptyEnv);
        Assert.True(options.Validate);
        Assert.Equal("Server=x;", options.ConnectionString);
    }

    // --- env precedence (ADR-0015: MSSQL_CONNECTION_STRING wins over --connection-string) ---
    // Validate is CLI-only by design — there is no MSSQL_VALIDATE env var.

    [Fact]
    public void Validate_Flag_NoEnvVar_Twin()
    {
        // Even if someone sets MSSQL_VALIDATE, it must be ignored — validate is one-shot CLI only.
        var env = Env(("MSSQL_VALIDATE", "true"));
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;" },
            env);
        Assert.False(options.Validate);
    }

    // --- ConnectionValidator: invalid connection string ---

    [Fact]
    public async Task ValidateAsync_InvalidConnectionString_ReturnsFalseWithObfuscatedMessage()
    {
        MssqlMcpOptions options = new()
        {
            ConnectionString = "Server=;Database=;InvalidKeyword=Yes;Password=hunter2;",
            RetryCount = 0,
            RetryIntervalMin = 0,
            RetryIntervalMax = 1,
        };

        (bool ok, string message) = await ConnectionValidator.ValidateAsync(options, CancellationToken.None);

        Assert.False(ok);
        Assert.StartsWith(ConnectionValidator.FailurePrefix, message);
        // Password value must never appear in the error message (ADR-0005).
        Assert.DoesNotContain("hunter2", message);
    }

    [Fact]
    public async Task ValidateAsync_NullOptions_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ConnectionValidator.ValidateAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void Validate_SuccessMessage_MatchesStartupFormat()
    {
        // ADR-0015 contract: startup messages are prefixed with [startup].
        Assert.StartsWith("[startup]", ConnectionValidator.SuccessMessage);
        Assert.StartsWith("[startup]", ConnectionValidator.FailurePrefix);
    }

    [Fact]
    public async Task ValidateAsync_CancelledToken_ReturnsFalseWithMessage()
    {
        MssqlMcpOptions options = new()
        {
            ConnectionString = "Server=localhost;Integrated Security=true;",
            RetryCount = 0,
            RetryIntervalMin = 0,
            RetryIntervalMax = 1,
        };

        using CancellationTokenSource cts = new();
        cts.Cancel();

        (bool ok, string message) = await ConnectionValidator.ValidateAsync(options, cts.Token);

        Assert.False(ok);
        Assert.StartsWith(ConnectionValidator.FailurePrefix, message);
    }

    // --- Connection string env-precedence applies even when --validate is set ---

    [Fact]
    public void Validate_With_EnvConnectionString_WinsOverCliFlag()
    {
        // ADR-0015: env-secret-over-flag for MSSQL_CONNECTION_STRING.
        var env = Env(("MSSQL_CONNECTION_STRING", "Server=env;"));
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=cli;", "--validate" },
            env);
        Assert.True(options.Validate);
        Assert.Equal("Server=env;", options.ConnectionString);
    }
}
