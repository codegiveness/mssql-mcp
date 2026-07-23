using mssql_mcp.Core.Configuration;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// Tests for <see cref="CliDispatch.Dispatch"/> — the pure pre-Parse arg dispatcher.
/// Verifies --version, --help/-h, and RunServer routing per ADR-0031.
/// </summary>
public class CliDispatchTests
{
    [Fact]
    public void Dispatch_VersionFlag_ReturnsVersion()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--version" });
        Assert.IsType<CliDispatchResult.Version>(result);
    }

    [Fact]
    public void Dispatch_HelpFlag_ReturnsHelp()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--help" });
        Assert.IsType<CliDispatchResult.Help>(result);
    }

    [Fact]
    public void Dispatch_ShortHelpFlag_ReturnsHelp()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "-h" });
        Assert.IsType<CliDispatchResult.Help>(result);
    }

    [Fact]
    public void Dispatch_HelpWithOtherFlags_ReturnsHelp()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--help", "--connection-string", "x" });
        Assert.IsType<CliDispatchResult.Help>(result);
    }

    [Fact]
    public void Dispatch_VersionWithHelp_ReturnsHelp()
    {
        // Help takes precedence over version per ADR-0031.
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--version", "--help" });
        Assert.IsType<CliDispatchResult.Help>(result);
    }

    [Fact]
    public void Dispatch_NoArgs_ReturnsRunServer()
    {
        CliDispatchResult result = CliDispatch.Dispatch(Array.Empty<string>());
        Assert.IsType<CliDispatchResult.RunServer>(result);
    }

    [Fact]
    public void Dispatch_ConnectionString_ReturnsRunServer()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--connection-string", "Server=x;" });
        Assert.IsType<CliDispatchResult.RunServer>(result);
    }

    [Fact]
    public void Dispatch_ConnectionStringConsumesNextTokenAsValue()
    {
        // --connection-string consumes the next token as its value,
        // so "upgrade" is not treated as an unknown flag here (that's #59).
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--connection-string", "upgrade" });
        Assert.IsType<CliDispatchResult.RunServer>(result);
    }

    [Fact]
    public void Dispatch_ValidateFlag_ReturnsRunServer()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--validate" });
        Assert.IsType<CliDispatchResult.RunServer>(result);
    }

    [Fact]
    public void Dispatch_ConnectionStringEqualsForm_DoesNotConsumeNextToken()
    {
        // --connection-string=x is self-contained; the next token is a real flag.
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--connection-string=x", "--help" });
        Assert.IsType<CliDispatchResult.Help>(result);
    }

    [Fact]
    public void Dispatch_ConnectionStringConsumesVersionAsValue()
    {
        // --connection-string --version: --version is the connection string value,
        // not a version flag. Should return RunServer.
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--connection-string", "--version" });
        Assert.IsType<CliDispatchResult.RunServer>(result);
    }

    [Fact]
    public void Dispatch_HelpAfterConnectionStringValue_ReturnsHelp()
    {
        // --connection-string x --help: --help comes after the consumed value.
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--connection-string", "x", "--help" });
        Assert.IsType<CliDispatchResult.Help>(result);
    }

    [Fact]
    public void UsageText_ContainsUpdateSection()
    {
        Assert.Contains("To update", CliDispatch.UsageText);
        Assert.Contains("npm install -g @codegiveness/mssql-mcp@latest", CliDispatch.UsageText);
        Assert.Contains("dotnet tool update -g codegiveness.mssql-mcp", CliDispatch.UsageText);
    }

    [Fact]
    public void UsageText_ContainsAllRecognizedFlags()
    {
        Assert.Contains("--version", CliDispatch.UsageText);
        Assert.Contains("--help", CliDispatch.UsageText);
        Assert.Contains("-h", CliDispatch.UsageText);
        Assert.Contains("--validate", CliDispatch.UsageText);
        Assert.Contains("--connection-string", CliDispatch.UsageText);
        Assert.Contains("--access-mode", CliDispatch.UsageText);
        Assert.Contains("--query-timeout", CliDispatch.UsageText);
        Assert.Contains("--log-level", CliDispatch.UsageText);
    }

    [Fact]
    public void UsageText_ContainsEnvVarHints()
    {
        Assert.Contains(MssqlMcpOptions.EnvConnectionString, CliDispatch.UsageText);
        Assert.Contains(MssqlMcpOptions.EnvAccessMode, CliDispatch.UsageText);
        Assert.Contains(MssqlMcpOptions.EnvQueryTimeout, CliDispatch.UsageText);
        Assert.Contains(MssqlMcpOptions.EnvLogLevel, CliDispatch.UsageText);
    }

    [Fact]
    public void UsageText_ContainsDefaults()
    {
        Assert.Contains("restricted", CliDispatch.UsageText);
        Assert.Contains("30", CliDispatch.UsageText);
        Assert.Contains("info", CliDispatch.UsageText);
    }

    [Fact]
    public void Dispatch_NullArgs_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CliDispatch.Dispatch(null!));
    }

    [Fact]
    public void Dispatch_BareUnknownArg_ReturnsUnknownArgument()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "upgrade" });
        CliDispatchResult.UnknownArgument unknown = Assert.IsType<CliDispatchResult.UnknownArgument>(result);
        Assert.Equal("upgrade", unknown.Argument);
    }

    [Fact]
    public void Dispatch_UnknownFlag_ReturnsUnknownArgument()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--bogus" });
        CliDispatchResult.UnknownArgument unknown = Assert.IsType<CliDispatchResult.UnknownArgument>(result);
        Assert.Equal("--bogus", unknown.Argument);
    }

    [Fact]
    public void Dispatch_VersionTakesPrecedenceOverUnknownArg()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--version", "upgrade" });
        Assert.IsType<CliDispatchResult.Version>(result);
    }

    [Fact]
    public void Dispatch_ConnectionStringEqualsUpgrade_ReturnsRunServer()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--connection-string=upgrade" });
        Assert.IsType<CliDispatchResult.RunServer>(result);
    }

    [Fact]
    public void Dispatch_ConnectionStringNoValue_LastArg_ReturnsRunServer()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--connection-string" });
        Assert.IsType<CliDispatchResult.RunServer>(result);
    }

    [Fact]
    public void Dispatch_HelpTakesPrecedenceOverUnknownArg()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "upgrade", "--help" });
        Assert.IsType<CliDispatchResult.Help>(result);
    }

    [Fact]
    public void Dispatch_UnknownArgBeforeHelp_HelpStillWins()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--help", "upgrade" });
        Assert.IsType<CliDispatchResult.Help>(result);
    }

    [Fact]
    public void Dispatch_UnknownFlagEqualsValue_ReportsFlagPartOnly()
    {
        CliDispatchResult result = CliDispatch.Dispatch(new[] { "--bogus=foo" });
        CliDispatchResult.UnknownArgument unknown = Assert.IsType<CliDispatchResult.UnknownArgument>(result);
        Assert.Equal("--bogus", unknown.Argument);
    }
}
