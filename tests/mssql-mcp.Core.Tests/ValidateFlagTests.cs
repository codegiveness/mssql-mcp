using System.Collections;
using System.Reflection;
using Microsoft.Data.SqlClient;
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
        // An unsupported keyword produces ArgumentException at parse time (before any network
        // call). The failure is classified by exception type → [argument]. Password obfuscation
        // still applies (ADR-0005).
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
        Assert.Contains("[argument]:", message);
        // Password value must never appear in the error message (ADR-0005).
        Assert.DoesNotContain("hunter2", message);
    }

    [Fact]
    public async Task ValidateAsync_BadHost_ReturnsFalseWithConnectionTag()
    {
        // Bad host never resolves — fails at OpenAsync with a SqlException whose Number is a
        // network-class code (e.g. 11001 = WSAHOST_NOT_FOUND). Classified as [connection].
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
        Assert.Contains("[connection]:", message);
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
        // A cancelled token surfaces as TaskCanceledException (subclass of
        // OperationCanceledException) → classified as [timeout].
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
        Assert.Contains("[timeout]:", message);
    }

    // --- ClassifyFailure: direct unit tests via fabricated SqlException (ticket 31) ---
    // SqlException has no public ctor; SqlExceptionFactory builds one through reflection, the
    // same pattern used in mssql-mcp.Tools.Tests/ExecuteSqlTests.cs.

    [Fact]
    public void ClassifyFailure_SqlException_Number18456_ReturnsAuth()
    {
        SqlException ex = SqlExceptionFactory.Create(number: 18456, message: "Login failed for user 'sa'.");
        Assert.Equal("auth", ConnectionValidator.ClassifyFailure(ex));
    }

    [Fact]
    public void ClassifyFailure_SqlException_NumberNeg2_ReturnsTimeout()
    {
        SqlException ex = SqlExceptionFactory.Create(number: -2, message: "Execution Timeout Expired.");
        Assert.Equal("timeout", ConnectionValidator.ClassifyFailure(ex));
    }

    [Fact]
    public void ClassifyFailure_SqlException_NumberNeg2076_ReturnsCertificate()
    {
        SqlException ex = SqlExceptionFactory.Create(number: -2076, message: "The certificate chain was issued by an authority that is not trusted.");
        Assert.Equal("certificate", ConnectionValidator.ClassifyFailure(ex));
    }

    [Theory]
    [InlineData(53)]
    [InlineData(64)]
    [InlineData(233)]
    [InlineData(10060)]
    public void ClassifyFailure_SqlException_ConnectionNumbers_ReturnsConnection(int number)
    {
        SqlException ex = SqlExceptionFactory.Create(number: number, message: "A network-related error occurred.");
        Assert.Equal("connection", ConnectionValidator.ClassifyFailure(ex));
    }

    [Fact]
    public void ClassifyFailure_SqlException_UnrecognizedNumber_ReturnsConnection()
    {
        // A SqlException during pre-flight validation that doesn't match a known auth/timeout/
        // certificate number is treated as connection-class — the validator's job is to confirm
        // reachability, so an unmatched SqlException is most likely a connection-layer failure.
        SqlException ex = SqlExceptionFactory.Create(number: 11001, message: "No such host is known.");
        Assert.Equal("connection", ConnectionValidator.ClassifyFailure(ex));
    }

    [Fact]
    public void ClassifyFailure_OperationCanceledException_ReturnsTimeout()
    {
        Assert.Equal("timeout", ConnectionValidator.ClassifyFailure(new TaskCanceledException()));
        Assert.Equal("timeout", ConnectionValidator.ClassifyFailure(new OperationCanceledException()));
    }

    [Fact]
    public void ClassifyFailure_TimeoutException_ReturnsTimeout()
    {
        Assert.Equal("timeout", ConnectionValidator.ClassifyFailure(new TimeoutException()));
    }

    [Fact]
    public void ClassifyFailure_ArgumentException_ReturnsArgument()
    {
        Assert.Equal("argument", ConnectionValidator.ClassifyFailure(new ArgumentException("Keyword not supported.")));
    }

    [Fact]
    public void ClassifyFailure_OtherException_ReturnsTypeNameLowercasedWithoutExceptionSuffix()
    {
        Assert.Equal("io", ConnectionValidator.ClassifyFailure(new IOException("disk full")));
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

/// <summary>
/// Builds a real <see cref="SqlException"/> via reflection for ClassifyFailure unit tests.
/// SqlException has no public constructor. Mirrors the helper in
/// mssql-mcp.Tools.Tests/ExecuteSqlTests.cs (kept local to avoid cross-project internals).
/// </summary>
internal static class SqlExceptionFactory
{
    public static SqlException Create(int number, string message, byte severity = 16, int line = 1, string? procedure = null)
    {
        Type sqlErrorType = typeof(SqlError);
        Type sqlErrorCollectionType = typeof(SqlErrorCollection);

        ConstructorInfo? errorCtor = sqlErrorType.GetConstructors(
            BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(c => c.GetParameters().Length == 9)
            ?? throw new InvalidOperationException("SqlError 9-arg ctor not found.");

        object error = errorCtor.Invoke(new object?[]
        {
            number,        // infoNumber
            (byte)1,       // errorState
            severity,      // errorClass
            "localhost",   // server
            message,       // errorMessage
            procedure,     // procedure
            line,          // lineNumber
            0,             // win32ErrorCode
            null,          // exception
        });

        ConstructorInfo? collectionCtor = sqlErrorCollectionType.GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)
            ?? throw new InvalidOperationException("SqlErrorCollection parameterless ctor not found.");
        object collection = collectionCtor.Invoke(null);

        MethodInfo? addMethod = sqlErrorCollectionType.GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("SqlErrorCollection.Add not found.");
        addMethod.Invoke(collection, new[] { error });

        Type sqlExceptionType = typeof(SqlException);
        ConstructorInfo? exCtor = sqlExceptionType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(c =>
            {
                ParameterInfo[] p = c.GetParameters();
                return p.Length == 4
                    && p[0].ParameterType == typeof(string)
                    && p[1].ParameterType == typeof(SqlErrorCollection)
                    && p[2].ParameterType == typeof(Exception)
                    && p[3].ParameterType == typeof(Guid);
            })
            ?? throw new InvalidOperationException("SqlException 4-arg ctor not found.");

        return (SqlException)exCtor.Invoke(new object?[]
        {
            message,
            collection,
            null,
            Guid.Empty,
        });
    }
}
