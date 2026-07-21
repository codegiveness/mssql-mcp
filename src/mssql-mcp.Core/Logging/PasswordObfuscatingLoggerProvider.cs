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
public sealed class PasswordObfuscatingLoggerProvider : ILoggerProvider
{
    private readonly ILoggerProvider _inner;

    public PasswordObfuscatingLoggerProvider(ILoggerProvider inner)
    {
        _inner = inner;
    }

    public ILogger CreateLogger(string categoryName) =>
        new ObfuscatingLogger(_inner.CreateLogger(categoryName));

    public void Dispose() => _inner.Dispose();

    private sealed class ObfuscatingLogger : ILogger
    {
        private readonly ILogger _inner;

        public ObfuscatingLogger(ILogger inner)
        {
            _inner = inner;
        }

        IDisposable? ILogger.BeginScope<TState>(TState state) => _inner.BeginScope(state);

        bool ILogger.IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            string original = formatter(state, exception);
            string obfuscated = PasswordObfuscator.Obfuscate(original);
            _inner.Log(
                logLevel,
                eventId,
                new ObfuscatedState(obfuscated),
                exception,
                static (s, e) => s.Message);
        }

        private sealed class ObfuscatedState(string message)
        {
            public string Message { get; } = message;
        }
    }
}
