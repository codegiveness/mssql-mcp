using System.Text;
using Microsoft.Extensions.Logging;

namespace mssql_mcp.Core.Logging;

/// <summary>
/// File sink for <see cref="MssqlMcpOptions.LogFile"/> per ADR-0011, with size-based
/// rotation per ADR-0030. Writes plain-text log lines (one per entry) to the configured
/// path. When the active file reaches <see cref="_maxBytes"/> bytes and
/// <see cref="_maxRolls"/> is positive, the file is closed and renamed through a
/// sequential chain (<c>&lt;path&gt;</c> → <c>&lt;path&gt;.1</c> → <c>&lt;path&gt;.2</c>
/// → …), with the oldest (<c>.{maxRolls}</c>) deleted first. <c>maxBytes=0</c> (or
/// <c>maxRolls=0</c>) disables rotation — the file grows unbounded, matching the
/// pre-ADR-0030 behavior.
///
/// Password obfuscation is applied here as a defense-in-depth measure, even though the
/// pipeline also installs <see cref="PasswordObfuscatingLoggerProvider"/>. This keeps the
/// file sink leak-proof regardless of which upstream provider routed the message.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _maxRolls;
    private readonly Lock _gate = new();
    private StreamWriter _writer;
    private bool _disposed;

    /// <summary>
    /// Opens <paramref name="path"/> in append mode with UTF-8 encoding. The parent
    /// directory must exist; the file is created if missing.
    /// </summary>
    public FileLoggerProvider(string path)
        : this(path, maxBytes: 0, maxRolls: 0)
    {
    }

    /// <summary>
    /// Opens <paramref name="path"/> in append mode with UTF-8 encoding and configures
    /// size-based rotation per ADR-0030. Rotation triggers when the active file reaches
    /// <paramref name="maxBytes"/> bytes; <paramref name="maxRolls"/> archived files are
    /// retained (<c>.1</c>..<c>.{maxRolls}</c>). <c>maxBytes=0</c> or <c>maxRolls=0</c>
    /// disables rotation entirely.
    /// </summary>
    public FileLoggerProvider(string path, long maxBytes, int maxRolls)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Must be non-negative.");
        }
        if (maxRolls < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRolls), "Must be non-negative.");
        }

        ValidatePath(path);

        _path = path;
        _maxBytes = maxBytes;
        _maxRolls = maxRolls;
        _writer = CreateWriter(path);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
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
            _writer.Flush();
            if (_maxBytes > 0 && _maxRolls > 0)
            {
                long length = _writer.BaseStream.Length;
                if (length >= _maxBytes)
                {
                    Rotate();
                }
            }
        }
    }

    // Sequential rename: close the active writer, delete .{maxRolls} if it exists,
    // rename .{n-1} → .{n} for n from maxRolls down to 1, rename <path> → <path>.1,
    // then open a fresh writer at <path>. Runs under _gate — the caller already holds it.
    private void Rotate()
    {
        _writer.Flush();
        _writer.Dispose();

        string oldest = $"{_path}.{_maxRolls}";
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (int n = _maxRolls; n > 1; n--)
        {
            string from = $"{_path}.{n - 1}";
            string to = $"{_path}.{n}";
            if (File.Exists(from))
            {
                File.Move(from, to, overwrite: true);
            }
        }

        File.Move(_path, $"{_path}.1", overwrite: true);

        _writer = CreateWriter(_path);
    }

    /// <summary>
    /// Defense-in-depth path validation (PB-2 hardening). Rejects paths containing
    /// <c>..</c> traversal segments and rejects symlinks whose resolved target differs
    /// from the configured path. Absolute paths are permitted — operators may use
    /// <c>/var/log/</c> or similar; no allowlist root is imposed.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> contains <c>..</c> or resolves via symlink to
    /// a different path than the one configured.
    /// </exception>
    private static void ValidatePath(string path)
    {
        // Reject traversal segments. Path.GetFullPath collapses them after the fact, so
        // we inspect the raw string — this catches both relative (../foo) and absolute
        // (/etc/../../foo) traversal shapes regardless of platform separator.
        if (path.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Log file path must not contain '..' traversal segments.",
                nameof(path));
        }

        // Resolve to absolute so the symlink check compares apples to apples. GetFullPath
        // normalizes separators and resolves relative segments against the CWD; traversal
        // was already rejected above, so this is safe.
        string resolved = Path.GetFullPath(path);

        // If the path is an existing symlink, reject when its target differs from the
        // configured path. FileInfo.LinkTarget returns the stored link target string for
        // symlinks (null for regular files or non-existent paths). We resolve the link
        // target to absolute and compare — a mismatch means the path points elsewhere.
        if (File.Exists(resolved) && new FileInfo(resolved).LinkTarget is { } linkTarget)
        {
            string resolvedLink = Path.GetFullPath(linkTarget);
            if (!string.Equals(resolvedLink, resolved, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Log file path '{path}' is a symlink pointing to '{linkTarget}', which differs from the configured path. Symlinks are rejected.",
                    nameof(path));
            }
        }
    }

    private static StreamWriter CreateWriter(string path)
    {
        ValidatePath(path);
        var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: false);
        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
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
        /// <summary>Singleton instance — NullScope has no state to dispose.</summary>
        public static NullScope Instance { get; } = new();
        /// <summary>No-op — NullScope holds no resources.</summary>
        public void Dispose() { }
    }
}
