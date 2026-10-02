using Microsoft.Extensions.DependencyInjection;
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

    private static void CreateSymbolicLink(string path, string target, bool directory = false)
    {
        try
        {
            if (directory)
            {
                Directory.CreateSymbolicLink(path, target);
            }
            else
            {
                File.CreateSymbolicLink(path, target);
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or PlatformNotSupportedException ||
            (OperatingSystem.IsWindows() && exception is IOException &&
             (exception.HResult & 0xffff) == 1314))
        {
            // Windows may require developer mode or the create-symbolic-link privilege.
            Assert.Skip($"Symbolic links are unavailable in this test environment: {exception.Message}");
        }
    }

    [Fact]
    public void LoggingContainer_DisposalReleasesFileForNextHost()
    {
        string dir = NewTempDir();
        try
        {
            string logPath = Path.Combine(dir, "host.log");
            for (int restart = 0; restart < 3; restart++)
            {
                var services = new ServiceCollection();
                services.AddLogging(builder => LoggingSetup.Configure(
                    builder, LogLevel.Information, logPath, 1024, 2));
                using (ServiceProvider container = services.BuildServiceProvider())
                {
                    ILogger logger = container.GetRequiredService<ILoggerFactory>().CreateLogger("Host");
                    logger.LogInformation("host lifecycle {Restart}", restart);
                }

                // An exclusive reopen fails while an old provider still owns the writer.
                using FileStream exclusive = File.Open(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            Assert.Contains("host lifecycle 2", File.ReadAllText(logPath));
        }
        finally { Cleanup(dir); }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedParent_RejectsBeforeDestinationFileCreation(bool targetExists)
    {
        string dir = NewTempDir();
        try
        {
            string destination = Path.Combine(dir, "destination");
            string nested = Path.Combine(destination, "nested");
            if (targetExists)
            {
                Directory.CreateDirectory(nested);
            }
            string parentLink = Path.Combine(dir, "linked-parent");
            CreateSymbolicLink(parentLink, destination, directory: true);
            string logPath = Path.Combine(parentLink, "nested", "app.log");

            Assert.Throws<ArgumentException>(() => new FileLoggerProvider(logPath));

            Assert.False(File.Exists(Path.Combine(nested, "app.log")));
            Assert.Equal(targetExists, Directory.Exists(destination));
            Assert.Equal(destination, new DirectoryInfo(parentLink).LinkTarget);
        }
        finally { Cleanup(dir); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalLink_RejectsExistingAndDanglingTargets(bool targetExists)
    {
        string dir = NewTempDir();
        try
        {
            string destination = Path.Combine(dir, "destination.log");
            if (targetExists)
            {
                File.WriteAllText(destination, "destination-must-stay-unchanged");
            }
            string logPath = Path.Combine(dir, "app.log");
            CreateSymbolicLink(logPath, destination);

            Assert.Throws<ArgumentException>(() => new FileLoggerProvider(logPath));

            if (targetExists)
            {
                Assert.Equal("destination-must-stay-unchanged", File.ReadAllText(destination));
            }
            else
            {
                Assert.False(File.Exists(destination));
            }
            Assert.Equal(destination, new FileInfo(logPath).LinkTarget);
        }
        finally { Cleanup(dir); }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void Rotation_RejectsLinkedArchiveBeforeMutatingChain(int linkedRoll, bool targetExists)
    {
        string dir = NewTempDir();
        try
        {
            string logPath = Path.Combine(dir, "app.log");
            using var provider = new FileLoggerProvider(logPath, maxBytes: 1, maxRolls: 3);
            string destination = Path.Combine(dir, "destination.log");
            if (targetExists)
            {
                File.WriteAllText(destination, "destination-must-stay-unchanged");
            }
            for (int roll = 1; roll <= 3; roll++)
            {
                if (roll == linkedRoll)
                {
                    CreateSymbolicLink($"{logPath}.{roll}", destination);
                }
                else
                {
                    File.WriteAllText($"{logPath}.{roll}", $"original-roll-{roll}");
                }
            }

            Assert.Throws<ArgumentException>(() => WriteLines(provider, 1, "rotation-attempt"));

            Assert.Contains("rotation-attempt", File.ReadAllText(logPath));
            for (int roll = 1; roll <= 3; roll++)
            {
                if (roll == linkedRoll)
                {
                    Assert.Equal(destination, new FileInfo($"{logPath}.{roll}").LinkTarget);
                }
                else
                {
                    Assert.Equal($"original-roll-{roll}", File.ReadAllText($"{logPath}.{roll}"));
                }
            }
            if (targetExists)
            {
                Assert.Equal("destination-must-stay-unchanged", File.ReadAllText(destination));
            }
            else
            {
                Assert.False(File.Exists(destination));
            }
        }
        finally { Cleanup(dir); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryPath_AppendsAndRotatesExistingFile(bool relativePath)
    {
        // Keep relative paths below CWD without changing process-global current directory.
        string dir = relativePath ? "mssql-mcp-log-tests-" + Guid.NewGuid().ToString("N") : NewTempDir();
        Directory.CreateDirectory(dir);
        try
        {
            string logPath = Path.Combine(dir, "app.log");
            File.WriteAllText(logPath, "existing-log-entry\n");
            using var provider = new FileLoggerProvider(logPath, maxBytes: 1, maxRolls: 2);
            WriteLines(provider, 1, "first-log-entry");
            WriteLines(provider, 1, "second-log-entry");
            provider.Dispose();

            Assert.Equal(string.Empty, File.ReadAllText(logPath));
            Assert.Contains("second-log-entry", File.ReadAllText(logPath + ".1"));
            string older = File.ReadAllText(logPath + ".2");
            Assert.StartsWith("existing-log-entry\n", older);
            Assert.Contains("first-log-entry", older);
            Assert.DoesNotContain("second-log-entry", older);
        }
        finally { Cleanup(dir); }
    }
}
