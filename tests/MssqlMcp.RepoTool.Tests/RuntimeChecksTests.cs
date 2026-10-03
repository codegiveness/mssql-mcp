using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MssqlMcp.RepoTool;

namespace MssqlMcp.RepoTool.Tests;

public sealed class RuntimeChecksTests
{
    private static readonly string[] ToolNames =
    [
        "list_databases", "list_schemas", "list_objects", "get_object_details", "execute_sql",
        "explain_query", "analyze_indexes", "get_top_queries", "analyze_db_health",
    ];

    public static bool SupportsSymbolicLinks => !OperatingSystem.IsWindows();

    [Fact]
    public void ToolContractAcceptsAllNineCorrectAnnotationsRegardlessOfOrder()
    {
        RuntimeChecks.ValidateToolsResponse(ToolResponse(ToolNames.Reverse()));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    public void ToolContractRejectsIncompleteOrSubstitutedTools(string mutation)
    {
        string[] names = (string[])ToolNames.Clone();
        names = mutation switch
        {
            "missing" => names[..^1],
            "duplicate" => [.. names[..^1], names[0]],
            _ => [.. names[..^1], "other_tool"],
        };
        Assert.Throws<InvalidOperationException>(() => RuntimeChecks.ValidateToolsResponse(ToolResponse(names)));
    }

    [Theory]
    [InlineData("list_databases", "false")]
    [InlineData("execute_sql", "true")]
    [InlineData("list_schemas", "\"true\"")]
    [InlineData("execute_sql", "null")]
    public void ToolContractRequiresExactBooleanAnnotations(string tool, string value)
    {
        var tools = ToolNames.Select(name => new Dictionary<string, object?>
        {
            ["name"] = name,
            ["annotations"] = new Dictionary<string, object?>
            {
                ["idempotentHint"] = name == tool ? JsonSerializer.Deserialize<JsonElement>(value) : name != "execute_sql",
            },
        });
        string response = JsonSerializer.Serialize(new { result = new { tools } });
        Assert.Throws<InvalidOperationException>(() => RuntimeChecks.ValidateToolsResponse(response));
    }

    [Theory]
    [InlineData("{\"error\":{\"message\":\"SECRET\"}}")]
    [InlineData("{\"result\":{\"tools\":null}}")]
    [InlineData("not JSON SECRET")]
    public void InvalidToolResponsesFailWithoutDisclosingTheirContent(string response)
    {
        Exception error = Assert.Throws<InvalidOperationException>(() => RuntimeChecks.ValidateToolsResponse(response));
        Assert.DoesNotContain("SECRET", error.Message);
    }

    [Fact]
    public void DatabaseResponseReadsTextFollowingOtherContent()
    {
        string response = JsonSerializer.Serialize(new
        {
            result = new
            {
                isError = false,
                content = new object[]
                {
                    new { type = "image", data = "ignored" },
                    new { type = "text", text = "[{\"name\":\"app\"},{\"name\":\"reporting\"}]" },
                },
            },
        });
        Assert.Equal(2, RuntimeChecks.ReadDatabaseCount(response));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("invalid SECRET")]
    public void DatabaseResponseRequiresNonemptyArray(string text)
    {
        string response = JsonSerializer.Serialize(new { result = new { content = new[] { new { type = "text", text } } } });
        Exception error = Assert.Throws<InvalidOperationException>(() => RuntimeChecks.ReadDatabaseCount(response));
        Assert.DoesNotContain("SECRET", error.Message);
    }

    [Theory]
    [InlineData("{\"result\":{\"isError\":true,\"content\":[{\"type\":\"text\",\"text\":\"[1]\"}]}}")]
    [InlineData("{\"result\":{\"content\":[]}}")]
    [InlineData("{\"error\":{\"message\":\"SECRET\"}}")]
    public void DatabaseResponseRejectsErrorsAndMissingContent(string response)
    {
        Assert.Throws<InvalidOperationException>(() => RuntimeChecks.ReadDatabaseCount(response));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1201")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.0")]
    [InlineData(" 1")]
    [InlineData("9999999999999999999999999")]
    public void DurationRejectsOutOfBoundsAndNondigitValues(string value)
    {
        Assert.Throws<ArgumentException>(() => RuntimeChecks.ParseDuration(value));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("1200", 1200)]
    [InlineData("00060", 60)]
    public void DurationAcceptsInclusiveBoundsAndDecimalLeadingZeroes(string value, int expected)
    {
        Assert.Equal(expected, RuntimeChecks.ParseDuration(value));
    }

    [Fact]
    public void ExistingFindingsPreventReuseWithoutMutation()
    {
        using var temporary = new TemporaryDirectory();
        string findings = Directory.CreateDirectory(Path.Combine(temporary.Path, "findings")).FullName;
        string artifact = Path.Combine(findings, "crash-real");
        File.WriteAllText(artifact, "SELECT broken");
        Assert.Throws<InvalidOperationException>(() => RuntimeChecks.PrepareOutput(temporary.Path));
        Assert.Equal("SELECT broken", File.ReadAllText(artifact));
        Assert.False(Directory.Exists(Path.Combine(temporary.Path, "corpus")));
    }

    [Fact]
    public void RetainedCorpusImportsBoundedTopLevelDataByContentHash()
    {
        using var temporary = new TemporaryDirectory();
        string retained = Directory.CreateDirectory(Path.Combine(temporary.Path, "retained")).FullName;
        string corpus = Directory.CreateDirectory(Path.Combine(temporary.Path, "corpus")).FullName;
        byte[] boundary = Enumerable.Repeat((byte)'x', 4096).ToArray();
        File.WriteAllBytes(Path.Combine(retained, "boundary"), boundary);
        File.WriteAllBytes(Path.Combine(retained, "duplicate"), boundary);
        File.WriteAllBytes(Path.Combine(retained, "oversized"), new byte[4097]);
        File.WriteAllText(Path.Combine(retained, "empty"), "");
        string nested = Directory.CreateDirectory(Path.Combine(retained, "nested")).FullName;
        File.WriteAllText(Path.Combine(nested, "not-a-top-level-sample"), "SELECT 2");

        RuntimeChecks.ImportRetainedCorpus(retained, corpus);

        string boundaryHash = Convert.ToHexStringLower(SHA256.HashData(boundary));
        string emptyHash = Convert.ToHexStringLower(SHA256.HashData(Array.Empty<byte>()));
        Assert.Equal(new[] { boundaryHash, emptyHash }.Order(), Directory.EnumerateFiles(corpus).Select(Path.GetFileName).Order());
        Assert.Equal(boundary, File.ReadAllBytes(Path.Combine(corpus, boundaryHash)));
        Assert.Empty(File.ReadAllBytes(Path.Combine(corpus, emptyHash)));
    }

    [Fact(Skip = "Native symlink creation requires privileges on Windows.", SkipUnless = nameof(SupportsSymbolicLinks))]
    public void RetainedCorpusDoesNotFollowDirectoryOrSampleSymlinks()
    {
        using var temporary = new TemporaryDirectory();
        string retained = Directory.CreateDirectory(Path.Combine(temporary.Path, "retained")).FullName;
        string corpus = Directory.CreateDirectory(Path.Combine(temporary.Path, "corpus")).FullName;
        string outside = Path.Combine(temporary.Path, "outside");
        File.WriteAllText(outside, "SELECT secret");
        File.CreateSymbolicLink(Path.Combine(retained, "linked-file"), outside);
        File.CreateSymbolicLink(Path.Combine(retained, "broken-file"), Path.Combine(temporary.Path, "absent"));
        RuntimeChecks.ImportRetainedCorpus(retained, corpus);
        Assert.Empty(Directory.EnumerateFileSystemEntries(corpus));
        File.WriteAllText(Path.Combine(retained, "regular-file"), "SELECT 1");

        string linked = Path.Combine(temporary.Path, "linked-directory");
        Directory.CreateSymbolicLink(linked, retained);
        RuntimeChecks.ImportRetainedCorpus(linked, corpus);
        Assert.Empty(Directory.EnumerateFileSystemEntries(corpus));

        string nested = Directory.CreateDirectory(Path.Combine(retained, "nested")).FullName;
        File.WriteAllText(Path.Combine(nested, "nested-sample"), "SELECT 2");
        RuntimeChecks.ImportRetainedCorpus(Path.Combine(linked, "nested"), corpus);
        Assert.Empty(Directory.EnumerateFileSystemEntries(corpus));
        Assert.Throws<InvalidOperationException>(() => RuntimeChecks.PrepareOutput(linked));
        Assert.Equal("SELECT secret", File.ReadAllText(outside));
    }

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    public void ProbeRequiresNonzeroExitArtifactAndManagedExceptionEvidence(int exitCode, bool artifact, bool managedEvidence)
    {
        using var temporary = new TemporaryDirectory();
        string probe = Directory.CreateDirectory(Path.Combine(temporary.Path, "probe")).FullName;
        string log = Path.Combine(temporary.Path, "probe.log");
        if (artifact)
        {
            File.WriteAllText(Path.Combine(probe, "crash-example"), "");
        }
        File.WriteAllText(log, managedEvidence ? "Intentional fuzz-engine crash-detection probe." : "Native engine exited.");
        Assert.Throws<InvalidOperationException>(() => RuntimeChecks.ValidateCrashProbe(exitCode, probe, log));
    }

    [Fact]
    public void ProbeAcceptsActualEmptyInputCrashArtifactWithManagedEvidence()
    {
        using var temporary = new TemporaryDirectory();
        string probe = Directory.CreateDirectory(Path.Combine(temporary.Path, "probe")).FullName;
        string log = Path.Combine(temporary.Path, "probe.log");
        File.WriteAllBytes(Path.Combine(probe, "crash-empty-input"), []);
        File.WriteAllText(log, "System.InvalidOperationException: Intentional fuzz-engine crash-detection probe.");
        RuntimeChecks.ValidateCrashProbe(77, probe, log);
    }

    private static string ToolResponse(IEnumerable<string> names) => JsonSerializer.Serialize(new
    {
        result = new { tools = names.Select(name => new { name, annotations = new { idempotentHint = name != "execute_sql" } }) },
    });

    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Path { get; } = Directory.CreateTempSubdirectory("runtime-checks-test-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
