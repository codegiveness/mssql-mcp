using System.Globalization;
using Microsoft.Extensions.Logging;

namespace mssql_mcp.Core.Logging;

/// <summary>
/// Maps the user-facing log level string (<see cref="MssqlMcpOptions.LogLevel"/>)
/// to <see cref="LogLevel"/> per ADR-0011. Accepted values: trace, debug, info,
/// warning, error, critical. Invalid values throw at startup with a [startup] prefix.
/// </summary>
public static class LogLevelParser
{
    /// <summary>
    /// Parses <paramref name="value"/> into a <see cref="LogLevel"/>.
    /// Throws <see cref="InvalidOperationException"/> with a [startup] prefix when the value
    /// is not recognized.
    /// </summary>
    public static LogLevel Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.ToLowerInvariant() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "info" => LogLevel.Information,
            "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "critical" => LogLevel.Critical,
            _ => throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "[startup] Invalid log level: {0}. Valid: trace, debug, info, warning, error, critical",
                    value)),
        };
    }
}
