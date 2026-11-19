using Microsoft.Extensions.Logging;

namespace mssql_mcp.Core.Logging;

/// <summary>
/// Wraps another <see cref="ILoggerProvider"/> and applies <see cref="PasswordObfuscator"/>
/// to every log message before the wrapped provider's formatter sees it. Used to wrap the
/// stderr console logger so that connection-string passwords never leak to stdout/stderr.
///
/// For the file sink, <see cref="FileLoggerProvider"/> already applies obfuscation internally;
/// wrapping it again is a no-op because the regex has no <c>Password=...;</c> match left.
/// </summary>
public sealed class PasswordObfuscatingLoggerProvider(ILoggerProvider inner) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new ObfuscatingLogger(inner.CreateLogger(categoryName));

    public void Dispose() => inner.Dispose();

    private sealed class ObfuscatingLogger(ILogger inner) : ILogger
    {
        IDisposable? ILogger.BeginScope<TState>(TState state) => inner.BeginScope(state);

        bool ILogger.IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            string original = formatter(state, exception);
            string obfuscated = PasswordObfuscator.Obfuscate(original);
            inner.Log(
                logLevel,
                eventId,
                new ObfuscatedState(obfuscated),
                null,
                static (s, e) => s.Message);
        }

        private sealed class ObfuscatedState(string message)
        {
            public string Message { get; } = message;
        }
    }
}
