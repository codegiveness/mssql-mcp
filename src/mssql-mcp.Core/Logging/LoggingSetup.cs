using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace mssql_mcp.Core.Logging;

/// <summary>
/// Wires up the mssql-mcp logging pipeline: stderr console sink (always), optional file sink
/// (when <see cref="MssqlMcpOptions.LogFile"/> is set), and password obfuscation applied to
/// every message on every sink. Kept out of Program.cs so the wiring is unit-testable.
/// </summary>
public static class LoggingSetup
{
    /// <summary>
    /// Configures <paramref name="builder"/> with:
    /// <list type="bullet">
    /// <item>Minimum level from <paramref name="logLevel"/> (parsed from CLI/env).</item>
    /// <item>Console logger writing to stderr (stdout is the MCP JSON-RPC channel),
    /// wrapped in <see cref="PasswordObfuscatingLoggerProvider"/>.</item>
    /// <item>File logger writing to <paramref name="logFile"/> if non-empty, also obfuscated.</item>
    /// </list>
    /// </summary>
    public static void Configure(ILoggingBuilder builder, LogLevel logLevel, string? logFile)
    {
        builder.SetMinimumLevel(logLevel);

        // Console to stderr — wrap the standard ConsoleLoggerProvider so every message runs
        // through PasswordObfuscator before the simple formatter writes it to stderr.
        var consoleOptions = new ConsoleLoggerOptions
        {
            LogToStandardErrorThreshold = LogLevel.Trace,
        };
        var optionsMonitor = new FixedOptionsMonitor<ConsoleLoggerOptions>(consoleOptions);
        var consoleProvider = new ConsoleLoggerProvider(optionsMonitor);
        builder.AddProvider(new PasswordObfuscatingLoggerProvider(consoleProvider));

        if (!string.IsNullOrEmpty(logFile))
        {
            // FileLoggerProvider applies PasswordObfuscator internally as defense-in-depth.
            builder.AddProvider(new FileLoggerProvider(logFile));
        }
    }

    /// <summary>
    /// Minimal <see cref="IOptionsMonitor{T}"/> returning a single fixed instance with no
    /// change notifications. Used to feed <see cref="ConsoleLoggerProvider"/> a pre-configured
    /// <see cref="ConsoleLoggerOptions"/> without spinning up the full Options subsystem.
    /// </summary>
    private sealed class FixedOptionsMonitor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T> : IOptionsMonitor<T>
    {
        private readonly T _value;

        public FixedOptionsMonitor(T value)
        {
            _value = value;
        }

        public T CurrentValue => _value;
        public T Get(string? name) => _value;

        public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static NullDisposable Instance { get; } = new();
            public void Dispose() { }
        }
    }
}
