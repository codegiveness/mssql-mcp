using System.Collections;
using mssql_mcp.Core.Configuration;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// Tests for MssqlMcpOptions.Parse — env+CLI precedence and validation per ADR-0015.
/// </summary>
public class OptionsTests
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

    [Fact]
    public void Parse_MissingConnectionString_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MssqlMcpOptions.Parse(Array.Empty<string>(), EmptyEnv));
        Assert.Contains("[startup]", ex.Message);
        Assert.Contains("connection string", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_CliConnectionString_UsedWhenEnvNotSet()
    {
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=localhost;Database=Test;" },
            EmptyEnv);
        Assert.Equal("Server=localhost;Database=Test;", options.ConnectionString);
    }

    [Fact]
    public void Parse_EnvConnectionString_WinsOverCli()
    {
        var env = Env(("MSSQL_CONNECTION_STRING", "Server=env;Database=Env;"));
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=cli;Database=Cli;" },
            env);
        Assert.Equal("Server=env;Database=Env;", options.ConnectionString);
    }

    [Fact]
    public void Parse_ConnectionString_EqualsForm_Parsed()
    {
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string=Server=eq;Database=Eq;" },
            EmptyEnv);
        Assert.Equal("Server=eq;Database=Eq;", options.ConnectionString);
    }

    [Fact]
    public void Parse_Defaults_Applied()
    {
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;" },
            EmptyEnv);
        Assert.Equal(AccessMode.Restricted, options.AccessMode);
        Assert.Equal(30, options.QueryTimeout);
        Assert.Equal("info", options.LogLevel);
        Assert.Null(options.LogFile);
        Assert.Equal(10 * 1024 * 1024L, options.MaxResultBytes);
        Assert.Equal(3, options.RetryCount);
        Assert.Equal(2, options.RetryIntervalMin);
        Assert.Equal(10, options.RetryIntervalMax);
    }

    [Fact]
    public void Parse_CliAccessMode_WinsOverEnv()
    {
        var env = Env(("MSSQL_ACCESS_MODE", "restricted"));
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;", "--access-mode", "unrestricted" },
            env);
        Assert.Equal(AccessMode.Unrestricted, options.AccessMode);
    }

    [Fact]
    public void Parse_EnvAccessMode_Applied()
    {
        var env = Env(("MSSQL_ACCESS_MODE", "unrestricted"));
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;" },
            env);
        Assert.Equal(AccessMode.Unrestricted, options.AccessMode);
    }

    [Fact]
    public void Parse_CliQueryTimeout_WinsOverEnv()
    {
        var env = Env(("MSSQL_QUERY_TIMEOUT", "60"));
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;", "--query-timeout", "90" },
            env);
        Assert.Equal(90, options.QueryTimeout);
    }

    [Fact]
    public void Parse_UnrestrictedMode_DefaultTimeoutIsZero()
    {
        var env = Env(("MSSQL_ACCESS_MODE", "unrestricted"));
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;" },
            env);
        Assert.Equal(0, options.QueryTimeout);
    }

    [Fact]
    public void Parse_InvalidAccessMode_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MssqlMcpOptions.Parse(
                new[] { "--connection-string", "Server=x;", "--access-mode", "bogus" },
                EmptyEnv));
        Assert.Contains("[startup]", ex.Message);
        Assert.Contains("access mode", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bogus", ex.Message);
        Assert.Contains("restricted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_InvalidEnvAccessMode_Throws()
    {
        var env = Env(("MSSQL_ACCESS_MODE", "nonsense"));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MssqlMcpOptions.Parse(
                new[] { "--connection-string", "Server=x;" },
                env));
        Assert.Contains("[startup]", ex.Message);
        Assert.Contains("nonsense", ex.Message);
    }

    [Fact]
    public void Parse_InvalidQueryTimeout_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MssqlMcpOptions.Parse(
                new[] { "--connection-string", "Server=x;", "--query-timeout", "abc" },
                EmptyEnv));
        Assert.Contains("query timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("abc", ex.Message);
    }

    [Fact]
    public void Parse_NegativeQueryTimeout_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MssqlMcpOptions.Parse(
                new[] { "--connection-string", "Server=x;", "--query-timeout", "-5" },
                EmptyEnv));
    }

    [Fact]
    public void Parse_CliLogLevel_WinsOverEnv()
    {
        var env = Env(("MSSQL_LOG_LEVEL", "info"));
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;", "--log-level", "debug" },
            env);
        Assert.Equal("debug", options.LogLevel);
    }

    [Fact]
    public void Parse_EnvLogFile_Applied()
    {
        var env = Env(("MSSQL_LOG_FILE", "/var/log/mssql-mcp.log"));
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;" },
            env);
        Assert.Equal("/var/log/mssql-mcp.log", options.LogFile);
    }

    [Fact]
    public void Parse_EnvMaxResultBytes_Applied()
    {
        var env = Env(("MSSQL_MAX_RESULT_BYTES", "5242880"));
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;" },
            env);
        Assert.Equal(5_242_880L, options.MaxResultBytes);
    }

    [Fact]
    public void Parse_EnvRetryCount_Applied()
    {
        var env = Env(("MSSQL_RETRY_COUNT", "5"));
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;" },
            env);
        Assert.Equal(5, options.RetryCount);
    }

    [Fact]
    public void Parse_EnvRetryInterval_Applied()
    {
        var env = Env(("MSSQL_RETRY_INTERVAL", "5"));
        var options = MssqlMcpOptions.Parse(
            new[] { "--connection-string", "Server=x;" },
            env);
        Assert.Equal(5, options.RetryIntervalMin);
    }

    [Fact]
    public void Parse_EnvVarNames_CaseInsensitive()
    {
        var env = Env(("mssql_connection_string", "Server=lower;"));
        var options = MssqlMcpOptions.Parse(Array.Empty<string>(), env);
        Assert.Equal("Server=lower;", options.ConnectionString);
    }

    [Fact]
    public void Parse_UnknownEnvVars_Ignored()
    {
        var env = Env(("MSSQL_UNKNOWN_VAR", "ignored"), ("MSSQL_CONNECTION_STRING", "Server=x;"));
        var options = MssqlMcpOptions.Parse(Array.Empty<string>(), env);
        Assert.Equal("Server=x;", options.ConnectionString);
    }
}
