using System.Text;
using Microsoft.Extensions.Logging;

namespace mssql_mcp.Core.Logging;

/// <summary>
/// File sink for <see cref="MssqlMcpOptions.LogFile"/> per ADR-0011. Writes plain-text
/// log lines (one per entry) to the configured path. No rotation — the file is appended
/// to on every run, and users wanting rotation configure logrotate externally.
///
/// Password obfuscation is applied here as a defense-in-depth measure, even though the
/// pipeline also installs <see cref="PasswordObfuscatingLoggerProvider"/>. This keeps the
/// file sink leak-proof regardless of which upstream provider routed the message.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly Lock _gate = new();

    /// <summary>
    /// Opens <paramref name="path"/> in append mode with UTF-8 encoding. The parent
    /// directory must exist; the file is created if missing.
    /// </summary>
    public FileLoggerProvider(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: false);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Flush();
            _writer.Dispose();
        }
    }

    internal void Write(LogLevel logLevel, string categoryName, string message, Exception? exception)
    {
        string obfuscated = PasswordObfuscator.Obfuscate(message);
        var sb = new StringBuilder(64 + obfuscated.Length + (exception?.Message.Length ?? 0));
        sb.Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(" [").Append(logLevel.ToString()).Append("] ");
        sb.Append(categoryName).Append(": ");
        sb.Append(obfuscated);
        if (exception is not null)
        {
            sb.Append(" | ").Append(exception.GetType().Name).Append(": ")
              .Append(PasswordObfuscator.Obfuscate(exception.Message));
        }
        sb.AppendLine();

        string line = sb.ToString();
        lock (_gate)
        {
            _writer.Write(line);
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _owner;
        private readonly string _categoryName;

        public FileLogger(FileLoggerProvider owner, string categoryName)
        {
            _owner = owner;
            _categoryName = categoryName;
        }

        IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

        bool ILogger.IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            string message = formatter(state, exception);
            _owner.Write(logLevel, _categoryName, message, exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
