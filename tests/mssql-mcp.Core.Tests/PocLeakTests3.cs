using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using mssql_mcp.Core.Logging;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// PoC tests written by security-research poc-engineer-b (Phase 3, independent verification)
/// for candidate findings NOT covered by existing tests:
///
///   PB-1: PasswordObfuscator regex timeout returns the ORIGINAL message — FALSIFIED.
///         Initial hypothesis: a pathological input forces RegexMatchTimeoutException,
///         triggering the catch block at PasswordObfuscator.cs:42-46 that returns `message`
///         unmodified. Empirical testing on .NET 10 shows the [GeneratedRegex] regex is
///         highly optimized: classic catastrophic-backtracking shapes (pure quotes, long
///         plain values, nested braces) complete in &lt;50ms even at 100MB. One million
///         `Password=secret;` entries (~16MB, wall-clock 341ms) still completes and
///         obfuscates ALL entries — the timeout exception does NOT fire. The .NET regex
///         timeout accounting differs from raw Stopwatch measurement; the 200ms cap is
///         per-match-attempt, not total wall-clock. The catch block is effectively dead
///         code for any realistic input.
///
///         The REAL obfuscation gap (partial-leak via alternation branch mismatch) is
///         covered by poc-engineer-a's PocObfuscationPartialLeakTests.cs (PA2/PA3/PA4).
///         This test file keeps only the falsification regression guard below.
///
///   PB-2: FileLoggerProvider performs NO path validation on the configured log file path.
///         MSSQL_LOG_FILE is read raw from the environment (MssqlMcpOptions.cs) and passed
///         unchanged into FileLoggerProvider, which opens it with FileMode.Append. If an
///         attacker can control the env var (container image, CI runner, compromised
///         operator shell), they can write log content to arbitrary locations — including
///         overwriting startup scripts, SSH authorized_keys (append), or polluting shared
///         volumes. This is a defense-in-depth gap, not a remote exploit: the env var is
///         trusted operator input, but the lack of any sanitization (no symlink reject,
///         no traversal reject, no absolute-path allowlist) means a single misconfigured
///         env grants arbitrary append.
/// </summary>
public class PocLeakTests3
{
    // ---------- PB-1: Regex timeout hypothesis — FALSIFIED ----------

    /// <summary>
    /// PB-1 FALSIFICATION: The regex timeout catch block (PasswordObfuscator.cs:42-46) is
    /// unreachable for realistic inputs on .NET 10. A 1M-entry input (~16MB) that takes
    /// 341ms wall-clock still completes and obfuscates ALL entries — no timeout exception.
    /// The 200ms matchTimeoutMilliseconds is per-match-attempt, not total execution time.
    ///
    /// This test is a regression guard: if a future .NET release regresses regex performance
    /// such that this input DOES time out, the test will fail and surface the change. The
    /// structural gap (return-original-on-timeout) remains in source regardless — it is a
    /// defense-in-depth concern only if the timeout ever fires.
    ///
    /// The real obfuscation gap (partial-leak) is covered by PocObfuscationPartialLeakTests.
    /// </summary>
    [Fact]
    public void PB1_RegexTimeoutHypothesis_Falsified_TimeoutDoesNotFire()
    {
        const int count = 1_000_000;
        string input = string.Concat(System.Linq.Enumerable.Repeat("Password=secret;", count));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string result = PasswordObfuscator.Obfuscate(input);
        sw.Stop();

        // The regex completed (wall-clock may exceed 200ms, but the timeout exception
        // did not fire — .NET measures matchTimeoutMilliseconds per-attempt, not total).
        Assert.NotEqual(input, result);

        // ALL entries were obfuscated — no cleartext password survives.
        Assert.DoesNotContain("Password=secret;", result, StringComparison.Ordinal);
        Assert.Contains("Password=***;", result, StringComparison.Ordinal);

        // Document the wall-clock time for future regression comparison.
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"regex took {sw.ElapsedMilliseconds}ms for 1M entries — investigate if this regresses");
    }

    // ---------- PB-2: FileLoggerProvider accepts arbitrary log file path ----------

    /// <summary>
    /// PB-2 INVERTED: FileLoggerProvider now rejects paths containing <c>..</c> traversal
    /// segments. Previously the constructor accepted such a path and wrote log content to
    /// the traversed location; hardening added a <c>..</c> reject in the constructor (and
    /// again in CreateWriter) so the provider throws ArgumentException before any file is
    /// opened. This test asserts the throw and that no file is created at the escape path.
    ///
    /// Attack scenario (mitigated): an operator sets
    /// MSSQL_LOG_FILE=../../.ssh/authorized_keys (append). Validation now rejects the path
    /// at startup instead of opening it for append.
    /// </summary>
    [Fact]
    public void PB2_FileLoggerProvider_RejectsTraversalPath_ThrowsArgumentException()
    {
        string guid = Guid.NewGuid().ToString("N");
        string baseDir = Path.Combine(Path.GetTempPath(), "mssql-mcp-poc-pb2-base-" + guid);
        string outsideDir = Path.Combine(Path.GetTempPath(), "mssql-mcp-poc-pb2-outside-" + guid);
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(outsideDir);
        try
        {
            // Construct a path inside baseDir that escapes via .. to outsideDir.
            // The raw (unresolved) path retains the `..` segment that ValidatePath rejects.
            string traversalPath = Path.Combine(baseDir, "..", "mssql-mcp-poc-pb2-outside-" + guid, "escape.log");
            string escapePath = Path.GetFullPath(traversalPath);

            // Constructor must throw ArgumentException for the traversal path.
            Assert.Throws<ArgumentException>(() =>
                new FileLoggerProvider(traversalPath, maxBytes: 0, maxRolls: 0));

            // No file was created at the escape path.
            Assert.False(File.Exists(escapePath), "no file should have been created at the traversed path");
        }
        finally
        {
            try { Directory.Delete(baseDir, recursive: true); } catch { }
            try { Directory.Delete(outsideDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// PB-2 ABSOLUTE PATH (still permitted): Absolute paths are explicitly allowed —
    /// operators may set MSSQL_LOG_FILE=/var/log/mssql-mcp.log. The path validation only
    /// rejects <c>..</c> traversal segments and symlinks; it does not impose an allowlist
    /// root directory. This test confirms a plain absolute path still constructs a working
    /// logger and writes log content.
    /// </summary>
    [Fact]
    public void PB2_FileLoggerProvider_AcceptsAbsolutePath_ValidatesAndCreatesLogger()
    {
        string absPath = Path.Combine(Path.GetTempPath(), "mssql-mcp-poc-pb2-abs-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            using var provider = new FileLoggerProvider(absPath, maxBytes: 0, maxRolls: 0);
            var logger = provider.CreateLogger("Test");
            logger.LogInformation("abs-path-log-line");
            provider.Dispose();

            Assert.True(File.Exists(absPath));
            Assert.Contains("abs-path-log-line", File.ReadAllText(absPath));
        }
        finally
        {
            try { File.Delete(absPath); } catch { }
        }
    }

    /// <summary>
    /// PB-2 INVERTED (static): The constructor now rejects traversal paths via
    /// ArgumentException (ValidatePath inspects the raw string for <c>..</c> segments).
    /// Previously this was a static proof that the constructor performed NO symlink or
    /// traversal check; hardening inverted it into a positive assertion that the
    /// constructor throws for a traversal path.
    /// </summary>
    [Fact]
    public void PB2_Constructor_RejectsTraversalPath()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "mssql-mcp-poc-pb2-static-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        try
        {
            string traversalPath = Path.Combine(baseDir, "..", "poc-static.log");
            // Constructor MUST throw ArgumentException for the traversal path.
            Assert.Throws<ArgumentException>(() =>
                new FileLoggerProvider(traversalPath, maxBytes: 0, maxRolls: 0));
        }
        finally
        {
            try { Directory.Delete(baseDir, recursive: true); } catch { }
            try { File.Delete(Path.Combine(Path.GetTempPath(), "poc-static.log")); } catch { }
        }
    }
}
