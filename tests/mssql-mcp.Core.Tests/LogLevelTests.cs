using Microsoft.Extensions.Logging;
using mssql_mcp.Core.Logging;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// Tests for <see cref="LogLevelParser"/> — verifies the string→<see cref="LogLevel"/> mapping
/// per ADR-0011, including case-insensitive input and fail-fast on invalid values per ADR-0015.
/// </summary>
public class LogLevelTests
{
    [Theory]
    [InlineData("trace", LogLevel.Trace)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("info", LogLevel.Information)]
    [InlineData("warning", LogLevel.Warning)]
    [InlineData("error", LogLevel.Error)]
    [InlineData("critical", LogLevel.Critical)]
    public void ParseLogLevel_ValidLevels(string input, LogLevel expected)
    {
        Assert.Equal(expected, LogLevelParser.Parse(input));
    }

    [Fact]
    public void ParseLogLevel_InvalidValue_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LogLevelParser.Parse("verbose"));
        Assert.Contains("[startup]", ex.Message);
        Assert.Contains("Invalid log level", ex.Message);
        Assert.Contains("verbose", ex.Message);
        Assert.Contains("trace", ex.Message);
        Assert.Contains("critical", ex.Message);
    }

    [Theory]
    [InlineData("INFO", LogLevel.Information)]
    [InlineData("Trace", LogLevel.Trace)]
    [InlineData("WARNING", LogLevel.Warning)]
    [InlineData("Error", LogLevel.Error)]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("Critical", LogLevel.Critical)]
    public void ParseLogLevel_CaseInsensitive(string input, LogLevel expected)
    {
        Assert.Equal(expected, LogLevelParser.Parse(input));
    }

    [Fact]
    public void ParseLogLevel_EmptyValue_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => LogLevelParser.Parse(string.Empty));
    }

    [Theory]
    [InlineData("info ")]
    [InlineData("info.")]
    public void ParseLogLevel_TrailingWhitespaceOrDot_Throws(string input)
    {
        // Whitespace is not trimmed — strict match keeps the contract simple and predictable.
        // (Whitespace in env vars is the user's mistake, surfaced as a [startup] error per ADR-0015.)
        Assert.Throws<InvalidOperationException>(() => LogLevelParser.Parse(input));
    }
}
