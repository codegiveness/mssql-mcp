using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MssqlMcp.RepoTool;

namespace MssqlMcp.RepoTool.Tests;

public sealed class SecurityCommandsTests
{
    public static bool SupportsSymbolicLinks => !OperatingSystem.IsWindows();

    [Fact]
    public void ContentPinsRejectModifiedDownloads()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "download");
        File.WriteAllText(path, "reviewed-content");
        var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        SecurityArchive.VerifySha256(path, digest, "fixture");
        File.AppendAllText(path, "tampered");
        Assert.Throws<SecurityPolicyException>(() => SecurityArchive.VerifySha256(path, digest, "fixture"));
    }

    [Fact]
    public void ExecutableExtractionCopiesOnlyTheReviewedRegularMember()
    {
        using var directory = new TestDirectory();
        using var archive = Tar(("../../outside", TarEntryType.RegularFile, "not-installed"),
            ("reviewed/tool", TarEntryType.RegularFile, "reviewed-binary"));
        var target = Path.Combine(directory.Path, "tool");
        SecurityArchive.ExtractExecutable(archive, "reviewed/tool", target);
        Assert.Equal("reviewed-binary", File.ReadAllText(target));
        Assert.Equal(new[] { target }, Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData(TarEntryType.SymbolicLink)]
    [InlineData(TarEntryType.HardLink)]
    [InlineData(TarEntryType.Directory)]
    public void ExecutableExtractionRejectsNonRegularMembers(TarEntryType type)
    {
        using var directory = new TestDirectory();
        using var archive = Tar(("tool", type, "other"));
        var target = Path.Combine(directory.Path, "tool");
        Assert.Throws<SecurityPolicyException>(() => SecurityArchive.ExtractExecutable(archive, "tool", target));
        Assert.False(File.Exists(target));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/drive")]
    [InlineData("..\\escape")]
    public void SourceArchiveRejectsTraversal(string member)
    {
        using var directory = new TestDirectory();
        using var archive = Tar((member, TarEntryType.RegularFile, "payload"));
        Assert.Throws<SecurityPolicyException>(() => SecurityArchive.ExtractSource(archive, directory.Path));
        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    [Theory]
    [InlineData(TarEntryType.SymbolicLink)]
    [InlineData(TarEntryType.HardLink)]
    public void SourceArchiveRejectsOutsideLinkTargets(TarEntryType type)
    {
        using var directory = new TestDirectory();
        using var archive = Tar(("link", type, "../outside"));
        Assert.Throws<SecurityPolicyException>(() => SecurityArchive.ExtractSource(archive, directory.Path));
        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    [Fact]
    public void SourceArchiveRejectsTraversalThroughLinksRegardlessOfOrder()
    {
        using var directory = new TestDirectory();
        using var archive = Tar(("alias/payload", TarEntryType.RegularFile, "payload"),
            ("alias", TarEntryType.SymbolicLink, "subdirectory"));
        Assert.Throws<SecurityPolicyException>(() => SecurityArchive.ExtractSource(archive, directory.Path));
        Assert.Null(new DirectoryInfo(Path.Combine(directory.Path, "alias")).LinkTarget);
    }

    [Fact]
    public void SourceArchiveResolvesDotDotAfterSymbolicLinksBeforeTrustingTargets()
    {
        using var directory = new TestDirectory();
        using var archive = Tar(("alias", TarEntryType.SymbolicLink, "."),
            ("escape", TarEntryType.SymbolicLink, "alias/../outside"));
        Assert.Throws<SecurityPolicyException>(() => SecurityArchive.ExtractSource(archive, directory.Path));
        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    [Fact]
    public void SourceArchiveRejectsCyclicLinksAndMissingHardLinkMembers()
    {
        using var directory = new TestDirectory();
        using var cycle = Tar(("first", TarEntryType.SymbolicLink, "second"), ("second", TarEntryType.SymbolicLink, "first"));
        Assert.Throws<SecurityPolicyException>(() => SecurityArchive.ExtractSource(cycle, directory.Path));
        using var missing = Tar(("alias", TarEntryType.HardLink, "missing"));
        Assert.Throws<SecurityPolicyException>(() => SecurityArchive.ExtractSource(missing, directory.Path));
    }

    [Fact(Skip = "Native symlink creation requires privileges on Windows.", SkipUnless = nameof(SupportsSymbolicLinks))]
    public void SourceArchiveSupportsContainedModuleLinksAndRegularHardLinkContents()
    {
        using var directory = new TestDirectory();
        using var archive = Tar(("workspace/module.js", TarEntryType.RegularFile, "module-content"),
            ("node_modules/module", TarEntryType.SymbolicLink, "../workspace"),
            ("module-copy.js", TarEntryType.HardLink, "workspace/module.js"));
        SecurityArchive.ExtractSource(archive, directory.Path);
        Assert.Equal("module-content", File.ReadAllText(Path.Combine(directory.Path, "node_modules/module/module.js")));
        Assert.Equal("../workspace", new DirectoryInfo(Path.Combine(directory.Path, "node_modules/module")).LinkTarget);
        Assert.Equal("module-content", File.ReadAllText(Path.Combine(directory.Path, "module-copy.js")));
        File.WriteAllText(Path.Combine(directory.Path, "workspace/module.js"), "changed-through-original");
        Assert.Equal("changed-through-original", File.ReadAllText(Path.Combine(directory.Path, "module-copy.js")));
    }

    [Fact]
    public void SourceArchiveRejectsSpecialDeviceMembers()
    {
        using var directory = new TestDirectory();
        using var archive = Tar(("device", TarEntryType.CharacterDevice, ""));
        Assert.Throws<SecurityPolicyException>(() => SecurityArchive.ExtractSource(archive, directory.Path));
        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    [Fact]
    public void SourceArchiveRejectsSameDirectoryWithAndWithoutTrailingSlash()
    {
        using var directory = new TestDirectory();
        using var archive = Tar(("module/", TarEntryType.Directory, ""), ("module", TarEntryType.Directory, ""));
        Assert.Throws<SecurityPolicyException>(() => SecurityArchive.ExtractSource(archive, directory.Path));
    }

    [Fact]
    public void ScannerEnvironmentRemovesAllWeakeningOverridesButNotUnrelatedVariables()
    {
        var environment = new Dictionary<string, string?>
        {
            ["GITLEAKS_CONFIG"] = "local-exceptions.toml",
            ["TRIVY_SKIP_DB_UPDATE"] = "true",
            ["TRIVY_SEVERITY"] = "CRITICAL",
            ["ZIZMOR_PERSONA"] = "pedantic",
            ["PATH"] = "/native/bin",
            ["TRIVYISH"] = "unrelated"
        };
        var removals = SecurityCommands.ScannerEnvironment(environment);
        Assert.Equal(4, removals.Count);
        Assert.All(environment.Keys.Where(key => key.Contains('_')), key => Assert.Null(removals[key]));
        Assert.False(removals.ContainsKey("PATH"));
        Assert.False(removals.ContainsKey("TRIVYISH"));
    }

    [Fact]
    public void FindingsPersistMetadataOnlyAndAlwaysReplaceSecretContent()
    {
        var findings = JsonNode.Parse("""
            [{"RuleID":"sql-password","File":"deleted.sql","StartLine":2,"Commit":"abc","Fingerprint":"abc:deleted.sql:sql-password:2",
              "Secret":"synthetic-sensitive-value","Match":"another-sensitive-value","Line":"sensitive-line","Message":"sensitive-commit"}]
            """)!.AsArray();
        var safe = SecurityCommands.SanitizeFindings(findings);
        SecurityCommands.RequireRedactedFindings(safe);
        Assert.Equal("REDACTED", safe[0]!["Secret"]!.GetValue<string>());
        Assert.Equal("deleted.sql", safe[0]!["File"]!.GetValue<string>());
        Assert.Equal(2, safe[0]!["StartLine"]!.GetValue<int>());
        Assert.Null(safe[0]!["Match"]);
        Assert.Null(safe[0]!["Line"]);
        Assert.Null(safe[0]!["Message"]);
        Assert.DoesNotContain("sensitive", safe.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal("synthetic-sensitive-value", findings[0]!["Secret"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("[{\"Secret\":\"not-redacted\"}]")]
    [InlineData("[{}]")]
    public void UnredactedOrMissingSecretsFailWithoutLeakingInput(string raw)
    {
        var error = Assert.Throws<SecurityPolicyException>(() => SecurityCommands.RequireRedactedFindings(JsonNode.Parse(raw)!.AsArray()));
        Assert.Equal("Gitleaks did not redact every finding", error.Message);
    }

    [Theory]
    [InlineData(0, 0, 1, true)]
    [InlineData(0, 1, 0, false)]
    [InlineData(0, 1, 1, true)]
    [InlineData(23, 1, 1, true)]
    [InlineData(24, 1, 0, true)]
    [InlineData(124, 1, 0, true)]
    [InlineData(1, 1, 0, true)]
    public void VulnerabilityGatesRejectEmptyAssessmentsFindingsEolAndScannerErrors(int exit, int packages, int vulnerabilities, bool failed)
    {
        var packageEntries = new JsonArray();
        var vulnerabilityEntries = new JsonArray();
        for (var index = 0; index < packages; index++) packageEntries.Add(new JsonObject { ["Name"] = "package" });
        for (var index = 0; index < vulnerabilities; index++) vulnerabilityEntries.Add(new JsonObject { ["Severity"] = "HIGH" });
        var document = new JsonObject { ["Results"] = new JsonArray(new JsonObject { ["Packages"] = packageEntries, ["Vulnerabilities"] = vulnerabilityEntries }) };
        Assert.Equal((failed, packages, vulnerabilities), SecurityCommands.AssessVulnerabilities(exit, document));
    }

    [Fact]
    public void AdvisoryMetadataIsRequiredAndCopiedFromTheInvocationCache()
    {
        using var cache = new TestDirectory();
        using var reports = new TestDirectory();
        Assert.Throws<SecurityPolicyException>(() => SecurityCommands.CopyDatabaseMetadata(cache.Path, reports.Path));
        Directory.CreateDirectory(Path.Combine(cache.Path, "db"));
        var metadata = "{\"SchemaVersion\":2,\"UpdatedAt\":\"2026-10-03T00:00:00Z\"}";
        File.WriteAllText(Path.Combine(cache.Path, "db/metadata.json"), metadata);
        SecurityCommands.CopyDatabaseMetadata(cache.Path, reports.Path);
        Assert.Equal(metadata, File.ReadAllText(Path.Combine(reports.Path, "database-metadata.json")));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("engines")]
    [InlineData("dependencies")]
    public void NpmSourceMustMatchAllReviewedRuntimeFields(string field)
    {
        var manifest = JsonNode.Parse("{\"version\":\"12.2.0\",\"engines\":{\"node\":\">=26\"},\"dependencies\":{\"tar\":\"^7.5.22\"}}")!.AsObject();
        var upstream = manifest.DeepClone().AsObject();
        upstream["name"] = "npm";
        SecurityCommands.ValidateNpmManifest(upstream, manifest);
        upstream[field] = "changed";
        Assert.Throws<SecurityPolicyException>(() => SecurityCommands.ValidateNpmManifest(upstream, manifest));
        upstream.Remove(field);
        Assert.Throws<SecurityPolicyException>(() => SecurityCommands.ValidateNpmManifest(upstream, manifest));
    }

    private static MemoryStream Tar(params (string Name, TarEntryType Type, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, leaveOpen: true))
        {
            foreach (var (name, type, content) in entries)
            {
                var entry = new PaxTarEntry(type, name);
                if (type == TarEntryType.RegularFile) entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
                if (type is TarEntryType.SymbolicLink or TarEntryType.HardLink) entry.LinkName = content;
                writer.WriteEntry(entry);
                entry.DataStream?.Dispose();
            }
        }
        stream.Position = 0;
        return stream;
    }

    private sealed class TestDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "security-tests-" + Guid.NewGuid().ToString("N"));
        internal TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
