using Microsoft.Extensions.Logging;
using mssql_mcp.Core.Logging;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// Tests for <see cref="FileLoggerProvider"/> size-based rotation per ADR-0030.
/// Verifies the active file rotates when it reaches maxBytes, archived rolls are named
/// .1...{maxRolls} with oldest deleted first, maxBytes=0 disables rotation, and password
/// obfuscation still applies to archives.
/// </summary>
public class FileLoggerProviderTests
{
    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mssql-mcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    private static void WriteLines(FileLoggerProvider provider, int count, string line)
    {
        var logger = provider.CreateLogger("Test");
        for (int i = 0; i < count; i++)
        {
            logger.LogInformation("{Line}", line);
        }
    }

    [Fact]
    public void UnderThreshold_NoRotation_ActiveFileHasAllLines_NoArchives()
    {
        string dir = NewTempDir();
        try
        {
            string logPath = Path.Combine(dir, "app.log");
            using var provider = new FileLoggerProvider(logPath, maxBytes: 1024, maxRolls: 3);
            WriteLines(provider, count: 3, line: "hello world");
            provider.Dispose();

            string active = File.ReadAllText(logPath);
            Assert.Contains("hello world", active);
            int lineCount = active.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.Equal(3, lineCount);
            Assert.False(File.Exists(logPath + ".1"), "no archive expected under threshold");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void AtThreshold_Rotates_ActiveIsFresh_ArchiveHasOldLines()
    {
        // Two ~85-byte lines cross a 100-byte threshold after the second write, producing
        // exactly one rotation: the two old lines move to .1, the active file is fresh.
        string dir = NewTempDir();
        try
        {
            string logPath = Path.Combine(dir, "app.log");
            using var provider = new FileLoggerProvider(logPath, maxBytes: 100, maxRolls: 3);
            WriteLines(provider, count: 2, line: "AAAA-padding-line-content-to-be-large-AAAAAAAAAA");
            provider.Dispose();

            Assert.True(File.Exists(logPath), "active file must always exist at the configured path");
            Assert.True(File.Exists(logPath + ".1"), "first archive must exist after rotation");
            Assert.False(File.Exists(logPath + ".2"), "only one rotation should produce a single .1 archive");

            string archive = File.ReadAllText(logPath + ".1");
            Assert.Contains("AAAA-padding-line-content", archive);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void MultipleRotations_RollCountRespected_OldestDeleted()
    {
        string dir = NewTempDir();
        try
        {
            string logPath = Path.Combine(dir, "app.log");
            using var provider = new FileLoggerProvider(logPath, maxBytes: 100, maxRolls: 2);
            WriteLines(provider, count: 50, line: "BBBB-padding-line-content-to-be-large-BBBBBBBBBB");
            provider.Dispose();

            Assert.True(File.Exists(logPath + ".1"), ".1 must exist");
            Assert.True(File.Exists(logPath + ".2"), ".2 must exist (maxRolls=2)");
            Assert.False(File.Exists(logPath + ".3"), ".3 must not exist (oldest deleted)");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void MaxBytesZero_DisablesRotation_FileGrowsUnbounded()
    {
        string dir = NewTempDir();
        try
        {
            string logPath = Path.Combine(dir, "app.log");
            using var provider = new FileLoggerProvider(logPath, maxBytes: 0, maxRolls: 3);
            WriteLines(provider, count: 20, line: "CCCC-padding-line-content-to-be-large-CCCCCCCCCC");
            provider.Dispose();

            Assert.True(File.Exists(logPath), "active file must exist");
            Assert.False(File.Exists(logPath + ".1"), "no archives when rotation is disabled");
            string active = File.ReadAllText(logPath);
            Assert.Contains("CCCC-padding-line-content", active);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void PasswordObfuscation_AppliesToArchiveAfterRotation()
    {
        // One password-bearing line (~98 bytes) crosses a 50-byte threshold immediately,
        // triggering exactly one rotation: the obfuscated password lands in .1, the
        // cleartext never appears in any file.
        string dir = NewTempDir();
        try
        {
            string logPath = Path.Combine(dir, "app.log");
            using var provider = new FileLoggerProvider(logPath, maxBytes: 50, maxRolls: 3);
            var logger = provider.CreateLogger("Test");
            logger.LogInformation("Connection: Server=x;Password=secret;Database=master;");
            provider.Dispose();

            Assert.True(File.Exists(logPath + ".1"), "archive must exist");
            string archive = File.ReadAllText(logPath + ".1");
            Assert.Contains("Password=***;", archive);
            Assert.DoesNotContain("Password=secret;", archive);

            // Defense-in-depth: cleartext must not appear in the active file either.
            if (File.Exists(logPath))
            {
                string active = File.ReadAllText(logPath);
                Assert.DoesNotContain("Password=secret;", active);
            }
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void MaxRollsZero_WithNonZeroMaxBytes_NoRotation()
    {
        // maxRolls=0 disables rotation: no archived files to retain, so the active file
        // is never renamed — matches the disabled contract (ADR-0030 ties rotation to
        // maxBytes=0; maxRolls=0 also yields a no-op since there is nowhere to roll to).
        string dir = NewTempDir();
        try
        {
            string logPath = Path.Combine(dir, "app.log");
            using var provider = new FileLoggerProvider(logPath, maxBytes: 100, maxRolls: 0);
            WriteLines(provider, count: 20, line: "EEEE-padding-line-content-to-be-large-EEEEEEEEEE");
            provider.Dispose();

            Assert.True(File.Exists(logPath));
            Assert.False(File.Exists(logPath + ".1"));
            string active = File.ReadAllText(logPath);
            Assert.Contains("EEEE-padding-line-content", active);
        }
        finally { Cleanup(dir); }
    }
}
