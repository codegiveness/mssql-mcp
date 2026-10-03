using System.Text.Json.Nodes;
using MssqlMcp.RepoTool;

namespace MssqlMcp.RepoTool.Tests;

public sealed class RepositoryChecksTests
{
    [Theory]
    [InlineData("v0.0.0")]
    [InlineData("v0.5.5")]
    [InlineData("v0.10.123")]
    [InlineData("v0.6.0-preview.1")]
    [InlineData("v0.6.0-0.alpha+build.1")]
    [InlineData("v0.999999999999999999999999.0")]
    public void ReleaseTagsPreserveAdmittedVersion(string tag)
    {
        var release = RepositoryChecks.ValidateReleaseTag(tag);
        Assert.Equal(tag, release.Name);
        Assert.Equal(tag[1..], release.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.5.5")]
    [InlineData("v0.5")]
    [InlineData("v00.5.5")]
    [InlineData("v0.05.5")]
    [InlineData("v0.5.05")]
    [InlineData("v0.5.5-01")]
    [InlineData("v0.5.5-")]
    [InlineData("v0.5.5+")]
    [InlineData("v0.5.5 preview")]
    [InlineData("v0.5.5\n")]
    [InlineData("v0.5.5\nversion=1.0.0")]
    [InlineData("v0.5.5; echo injected")]
    [InlineData("v0.5.5$(id)")]
    [InlineData("v1.0.0")]
    [InlineData("v1.0.0-rc.1")]
    [InlineData("v2.0.0")]
    [InlineData("v999999999999999999999.0.0")]
    public void ReleaseRejectsMalformedAndNonzeroMajorTags(string? tag) =>
        Assert.Throws<InvalidOperationException>(() => RepositoryChecks.ValidateReleaseTag(tag));

    [Theory]
    [InlineData("v0.5.5", true)]
    [InlineData("v1.0.0", false)]
    [InlineData("v1.0.0-rc.1", false)]
    [InlineData("v0.5.5\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ManualDispatchNeverFallsBackAndWritesOutputsOnlyAfterAcceptance(string? tag, bool admitted)
    {
        using var repo = new FixtureRepository();
        var output = repo.PathOf("outputs");
        File.WriteAllText(output, "existing\n");
        var environment = new Dictionary<string, string?>
        {
            ["GITHUB_EVENT_NAME"] = "workflow_dispatch",
            ["RELEASE_TAG"] = tag,
            ["GITHUB_REF_NAME"] = "v0.9.0",
            ["GITHUB_OUTPUT"] = output
        };
        if (admitted)
        {
            Assert.Equal("0.5.5", RepositoryChecks.CheckReleasePolicy(repo.Root, ["--github-event"], environment).Version);
            Assert.Equal("existing\nname=v0.5.5\nversion=0.5.5\n", File.ReadAllText(output));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => RepositoryChecks.CheckReleasePolicy(repo.Root, ["--github-event"], environment));
            Assert.Equal("existing\n", File.ReadAllText(output));
        }
    }

    [Fact]
    public void TagPushUsesRefAndBranchPushCannotRelease()
    {
        using var repo = new FixtureRepository();
        var environment = new Dictionary<string, string?>
        {
            ["GITHUB_EVENT_NAME"] = "push",
            ["GITHUB_REF_TYPE"] = "tag",
            ["GITHUB_REF_NAME"] = "v0.5.5",
            ["RELEASE_TAG"] = "v1.0.0"
        };
        Assert.Equal("0.5.5", RepositoryChecks.CheckReleasePolicy(repo.Root, ["--github-event"], environment).Version);
        environment["GITHUB_REF_NAME"] = "v1.0.0";
        Assert.Throws<InvalidOperationException>(() => RepositoryChecks.CheckReleasePolicy(repo.Root, ["--github-event"], environment));
        environment["GITHUB_REF_NAME"] = "v0.5.5";
        environment["GITHUB_REF_TYPE"] = "branch";
        Assert.Throws<InvalidOperationException>(() => RepositoryChecks.CheckReleasePolicy(repo.Root, ["--github-event"], environment));
        environment["GITHUB_EVENT_NAME"] = "pull_request";
        Assert.Throws<InvalidOperationException>(() => RepositoryChecks.CheckReleasePolicy(repo.Root, ["--github-event"], environment));
    }

    [Theory]
    [InlineData("0.5.5", true)]
    [InlineData("1.0.0", false)]
    [InlineData("1.0.0-rc.1", false)]
    [InlineData("0.05.5", false)]
    [InlineData(null, false)]
    public void ManifestGateUsesOnlyManifestVersion(string? version, bool admitted)
    {
        using var repo = new FixtureRepository();
        repo.WriteJson(".release-please-manifest.json", new JsonObject { ["."] = version });
        var environment = new Dictionary<string, string?>();
        foreach (var args in new[] { new[] { "--manifest" }, new[] { "--manifest", ".release-please-manifest.json" } })
        {
            if (admitted) Assert.Equal(version, RepositoryChecks.CheckReleasePolicy(repo.Root, args, environment).Version);
            else Assert.Throws<InvalidOperationException>(() => RepositoryChecks.CheckReleasePolicy(repo.Root, args, environment));
        }
    }

    [Theory]
    [InlineData("csproj")]
    [InlineData("package")]
    [InlineData("optional-linux-x64")]
    [InlineData("optional-linux-arm64")]
    [InlineData("optional-osx-x64")]
    [InlineData("optional-osx-arm64")]
    [InlineData("optional-win-x64")]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    [InlineData("osx-x64")]
    [InlineData("osx-arm64")]
    [InlineData("win-x64")]
    [InlineData("server")]
    [InlineData("npm")]
    [InlineData("nuget")]
    public async Task EveryStampMismatchFailsTheCommand(string stamp)
    {
        using var repo = new FixtureRepository();
        Assert.Empty(RepositoryChecks.CheckVersionConsistency(repo.Root));
        repo.Drift(stamp);
        Assert.Single(RepositoryChecks.CheckVersionConsistency(repo.Root));
        Assert.Equal(1, await RepositoryChecks.RunAsync("check-version-consistency", repo.Root, []));
    }

    [Fact]
    public void AllPlatformFailuresAndMainDriftAreReportedTogether()
    {
        using var repo = new FixtureRepository();
        repo.Drift("package");
        repo.Drift("linux-x64");
        repo.Drift("linux-arm64");
        repo.Write("npm/platforms/osx-x64/package.json", "{}");
        repo.Write("npm/platforms/osx-arm64/package.json", "{malformed");
        File.Delete(repo.PathOf("npm/platforms/win-x64/package.json"));
        var errors = RepositoryChecks.CheckVersionConsistency(repo.Root);
        Assert.Equal(6, errors.Count);
        foreach (var platform in FixtureRepository.Platforms)
            Assert.Contains(errors, error => error.Contains($"platforms/{platform}/package.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(".release-please-manifest.json")]
    [InlineData("npm/package.json")]
    [InlineData("server.json")]
    [InlineData("npm/platforms/win-x64/package.json")]
    public async Task MissingAndMalformedStampFilesFailCheckAndSync(string file)
    {
        using var repo = new FixtureRepository();
        repo.Write(file, "{invalid");
        Assert.NotEmpty(RepositoryChecks.CheckVersionConsistency(repo.Root));
        Assert.Equal(1, await RepositoryChecks.RunAsync("sync-all-stamps", repo.Root, []));
        File.Delete(repo.PathOf(file));
        Assert.NotEmpty(RepositoryChecks.CheckVersionConsistency(repo.Root));
        Assert.Equal(1, await RepositoryChecks.RunAsync("sync-all-stamps", repo.Root, []));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\".\":null}")]
    [InlineData("{\".\":5}")]
    [InlineData("{\".\":\"\"}")]
    public async Task ManifestMustProvideAStringAuthorityBeforeSyncWrites(string manifest)
    {
        using var repo = new FixtureRepository();
        repo.Write(".release-please-manifest.json", manifest);
        var before = repo.StampBytes();
        Assert.Single(RepositoryChecks.CheckVersionConsistency(repo.Root));
        Assert.Equal(1, await RepositoryChecks.RunAsync("sync-all-stamps", repo.Root, []));
        Assert.Equal(before, repo.StampBytes());
    }

    [Theory]
    [InlineData("<not valid xml")]
    [InlineData("<Project />")]
    public async Task MissingVersionPrefixAndMissingProjectRejectSynchronization(string project)
    {
        using var repo = new FixtureRepository();
        const string path = "src/mssql-mcp/mssql-mcp.csproj";
        repo.Write(path, project);
        var before = repo.StampBytes();
        Assert.Single(RepositoryChecks.CheckVersionConsistency(repo.Root));
        Assert.Equal(1, await RepositoryChecks.RunAsync("sync-all-stamps", repo.Root, []));
        Assert.Equal(before, repo.StampBytes());
        File.Delete(repo.PathOf(path));
        Assert.Single(RepositoryChecks.CheckVersionConsistency(repo.Root));
        Assert.Equal(1, await RepositoryChecks.RunAsync("sync-all-stamps", repo.Root, []));
    }

    [Fact]
    public async Task SynchronizationRepairsAllStampsPreservesMetadataAndIsByteIdempotent()
    {
        using var repo = new FixtureRepository();
        var before = repo.ReadJson("server.json");
        var expected = before.DeepClone().AsObject();
        const string version = "0.6.0-preview.1+build.2";
        expected["version"] = version;
        expected["packages"]![0]!["version"] = version;
        expected["packages"]![1]!["version"] = version;
        repo.WriteJson(".release-please-manifest.json", new JsonObject { ["."] = version });
        Assert.Equal(0, await RepositoryChecks.RunAsync("sync-all-stamps", repo.Root, []));
        Assert.Empty(RepositoryChecks.CheckVersionConsistency(repo.Root));
        Assert.True(JsonNode.DeepEquals(expected, repo.ReadJson("server.json")));
        Assert.Contains("<Other>keep</Other>", File.ReadAllText(repo.PathOf("src/mssql-mcp/mssql-mcp.csproj")), StringComparison.Ordinal);
        foreach (var platform in FixtureRepository.Platforms)
        {
            var package = repo.ReadJson($"npm/platforms/{platform}/package.json");
            Assert.Equal(version, package["version"]!.GetValue<string>());
            Assert.Equal(platform, package["name"]!.GetValue<string>());
            Assert.Equal("fixture-cpu", package["cpu"]![0]!.GetValue<string>());
            Assert.Equal("mssql-mcp", package["files"]![0]!.GetValue<string>());
        }
        Assert.Equal("fixture-main", repo.ReadJson("npm/package.json")["name"]!.GetValue<string>());
        var bytes = repo.StampBytes();
        Assert.Empty(RepositoryChecks.SyncAllStamps(repo.Root).ChangedFiles);
        Assert.Equal(bytes, repo.StampBytes());
    }

    [Theory]
    [InlineData("{\"packages\":null}")]
    [InlineData("{\"packages\":[]}")]
    [InlineData("{\"packages\":[{}]}")]
    [InlineData("{\"packages\":[null,{}]}")]
    public async Task SynchronizationRequiresBothServerPackages(string server)
    {
        using var repo = new FixtureRepository();
        repo.Write("server.json", server);
        Assert.Equal(1, await RepositoryChecks.RunAsync("sync-all-stamps", repo.Root, []));
        Assert.Equal(server, File.ReadAllText(repo.PathOf("server.json")));
    }

    [Fact]
    public void CheckCollectsCrossFileDriftAndChecksAdditionalServerPackages()
    {
        using var repo = new FixtureRepository();
        repo.Drift("csproj");
        repo.Drift("optional-linux-x64");
        repo.Drift("server");
        repo.Mutate("server.json", obj => obj["packages"]!.AsArray().Add(new JsonObject { ["registryType"] = "extra", ["version"] = "0.4.2" }));
        Assert.Equal(4, RepositoryChecks.CheckVersionConsistency(repo.Root).Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"dependencies\":[]}")]
    [InlineData("{\"dependencies\":{}}")]
    [InlineData("{\"dependencies\":{\"net10.0\":null}}")]
    [InlineData("{\"dependencies\":{\"net10.0\":{}}}")]
    [InlineData("{\"dependencies\":{\"net10.0\":\"invalid\"}}")]
    [InlineData("{invalid")]
    public async Task InvalidDependencyInventoriesFailClosed(string inventory)
    {
        using var repo = new FixtureRepository();
        repo.Write("packages.lock.json", inventory);
        Assert.Equal(1, await RepositoryChecks.RunAsync("check-redistribution", repo.Root, ["packages.lock.json"]));
        File.Delete(repo.PathOf("packages.lock.json"));
        Assert.Equal(1, await RepositoryChecks.RunAsync("check-redistribution", repo.Root, ["packages.lock.json"]));
    }

    [Theory]
    [InlineData("{\"resolved\":\"0.20.6\"}")]
    [InlineData("null")]
    public async Task HeldDependencyInAnyGraphBlocksPublication(string broker)
    {
        using var repo = new FixtureRepository();
        var lockfile = new JsonObject
        {
            ["dependencies"] = new JsonObject
            {
                ["net10.0"] = new JsonObject { ["Microsoft.Data.SqlClient"] = new JsonObject { ["resolved"] = "7.1.1" } },
                ["net10.0/linux-x64"] = new JsonObject { ["Microsoft.Identity.Client.NativeInterop"] = JsonNode.Parse(broker) }
            }
        };
        repo.WriteJson("packages.lock.json", lockfile);
        Assert.Equal(1, await RepositoryChecks.RunAsync("check-redistribution", repo.Root, ["packages.lock.json"]));
        lockfile["dependencies"]!.AsObject().Remove("net10.0/linux-x64");
        repo.WriteJson("packages.lock.json", lockfile);
        Assert.Equal(0, await RepositoryChecks.RunAsync("check-redistribution", repo.Root, ["packages.lock.json"]));
    }

    [Fact]
    public void ReadmeAcceptsCommentedConfigsTrailingCommasAndLiteralUrls()
    {
        var readme = """
            ```jsonc
            {"mcpServers":{"sql":{"command":"dotnet","env":{"URL":"https://example.test/a//b"},}},} // explanation
            ```
            ```json
            {"mcp":{"sql":{"command":["dotnet","server.dll"],"args":[]}}}
            ```
            ```json
            {"toolResult":{"rows":[]},"message":"// literal"}
            ```
            ![badge](https://example.test/badge.svg)
            """;
        var result = RepositoryChecks.LintReadme(readme);
        Assert.Equal(3, result.BlockCount);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("{invalid}")]
    [InlineData("{\"mcpServers\":{\"sql\":{\"args\":[]}}}")]
    [InlineData("{\"mcpServers\":{\"sql\":{\"command\":\"dotnet\"}}}")]
    [InlineData("{\"mcp\":{\"sql\":{\"command\":\"\",\"env\":{}}}}")]
    [InlineData("{\"mcp\":{\"sql\":null}}")]
    public void ReadmeRejectsInvalidSyntaxAndIncompleteBothClientConfigShapes(string block)
    {
        var result = RepositoryChecks.LintReadme($"```json\n{block}\n```");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task ReadmeCommandReportsAllFailuresAndRejectsNonHttpBadgeImages()
    {
        using var repo = new FixtureRepository();
        const string readme = "```json\n{bad}\n```\n```jsonc\n{\"mcpServers\":{\"sql\":{}}}\n```\n![badge](file:///badge.svg)";
        repo.Write("README.md", readme);
        Assert.Equal(4, RepositoryChecks.LintReadme(readme).Errors.Count);
        Assert.Equal(1, await RepositoryChecks.RunAsync("lint-readme-snippets", repo.Root, []));
    }

    [Fact]
    public void CoveragePercentagesRoundHalfwayUp()
    {
        var (report, npm) = CoverageFixture();
        report["summary"] = Metric("summary", 1, 16, 1, 80);
        Assert.Contains("| Production Core + Tools | 6.3% (1/16) | 1.3% (1/80) |",
            RepositoryChecks.SummarizeCoverage(report, npm), StringComparison.Ordinal);
    }

    [Fact]
    public void CoverageCombinesNestedGuardAndAsyncClassesWithoutPrefixFalseMatches()
    {
        var (report, npm) = CoverageFixture();
        var core = report["coverage"]!["assemblies"]![0]!["classesinassembly"]!.AsArray();
        foreach (var separator in new[] { "+", "/", "." })
            core.Add(Metric("mssql_mcp.Core.Guard.SqlGuard" + separator + "Nested", 2, 4, 1, 2));
        core.Add(Metric("mssql_mcp.Core.Guard.SqlGuardian", 100, 100, 100, 100));
        var result = RepositoryChecks.SummarizeCoverage(report, npm);
        Assert.Contains("| `src/mssql-mcp.Core/Guard/SqlGuard.cs` | 50.0% (8/16) | 50.0% (4/8) |", result, StringComparison.Ordinal);
        Assert.Contains("| `npm/bin/mssql-mcp.js` (download/cache/archive boundaries) | 75.0% (3/4) | 50.0% (1/2) |", result, StringComparison.Ordinal);
        Assert.Contains("| Production Core + Tools | 50.0% (10/20) | 50.0% (5/10) |", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing-core")]
    [InlineData("missing-tools")]
    [InlineData("missing-critical")]
    [InlineData("zero-critical")]
    [InlineData("zero-overall")]
    [InlineData("missing-npm")]
    [InlineData("zero-npm")]
    [InlineData("fraction")]
    [InlineData("negative")]
    [InlineData("unsafe-integer")]
    [InlineData("overcovered")]
    [InlineData("missing-count")]
    public void CoverageRejectsAbsentInstrumentationAndInvalidCounts(string failure)
    {
        var (report, npm) = CoverageFixture();
        var assemblies = report["coverage"]!["assemblies"]!.AsArray();
        var classes = assemblies[0]!["classesinassembly"]!.AsArray();
        switch (failure)
        {
            case "missing-core": assemblies.RemoveAt(0); break;
            case "missing-tools": assemblies.RemoveAt(1); break;
            case "missing-critical": classes.RemoveAt(0); break;
            case "zero-critical": classes[0] = Metric("mssql_mcp.Core.Guard.SqlGuard", 0, 0, 0, 0); break;
            case "zero-overall": report["summary"] = Metric("summary", 0, 0, 0, 0); break;
            case "missing-npm": npm.Clear(); break;
            case "zero-npm": npm.First().Value!["branches"]!["covered"] = 0; npm.First().Value!["branches"]!["total"] = 0; break;
            case "fraction": report["summary"]!["coveredlines"] = 1.5; break;
            case "negative": report["summary"]!["coveredlines"] = -1; break;
            case "unsafe-integer": report["summary"]!["coverablelines"] = 9007199254740992L; break;
            case "overcovered": report["summary"]!["coveredlines"] = 21; break;
            case "missing-count": report["summary"]!.AsObject().Remove("totalbranches"); break;
        }
        Assert.Throws<InvalidOperationException>(() => RepositoryChecks.SummarizeCoverage(report, npm));
    }

    [Fact]
    public async Task InvalidCoverageCannotOverwriteAnExistingPublishedSummary()
    {
        using var repo = new FixtureRepository();
        repo.Write("report/Summary.json", "{}");
        repo.Write("npm-coverage.json", "{}");
        repo.Write("report/critical-summary.md", "previous\n");
        Assert.Equal(1, await RepositoryChecks.RunAsync("coverage-summary", repo.Root, ["report/Summary.json", "npm-coverage.json"]));
        Assert.Equal("previous\n", File.ReadAllText(repo.PathOf("report/critical-summary.md")));
    }

    private static JsonObject Metric(string name, long lines = 2, long totalLines = 4, long branches = 1, long totalBranches = 2) => new()
    {
        ["name"] = name,
        ["coveredlines"] = lines,
        ["coverablelines"] = totalLines,
        ["coveredbranches"] = branches,
        ["totalbranches"] = totalBranches
    };

    private static (JsonObject Report, JsonObject Npm) CoverageFixture() =>
    (
        new JsonObject
        {
            ["summary"] = Metric("summary", 10, 20, 5, 10),
            ["coverage"] = new JsonObject
            {
                ["assemblies"] = new JsonArray
                {
                    new JsonObject { ["name"] = "mssql-mcp.Core", ["classesinassembly"] = new JsonArray
                    {
                        Metric("mssql_mcp.Core.Guard.SqlGuard"), Metric("mssql_mcp.Core.Logging.PasswordObfuscator"),
                        Metric("mssql_mcp.Core.ResultByteBudget"), Metric("mssql_mcp.Core.SqlExecutor+AsyncStateMachine")
                    } },
                    new JsonObject { ["name"] = "mssql-mcp.Tools", ["classesinassembly"] = new JsonArray { Metric("mssql_mcp.Tools.ToolErrors") } }
                }
            }
        },
        new JsonObject
        {
            [@"C:\repo\npm\bin\mssql-mcp.js"] = new JsonObject
            {
                ["lines"] = new JsonObject { ["covered"] = 3, ["total"] = 4 },
                ["branches"] = new JsonObject { ["covered"] = 1, ["total"] = 2 }
            }
        }
    );

    private sealed class FixtureRepository : IDisposable
    {
        internal static readonly string[] Platforms = ["linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64"];
        internal string Root { get; } = Directory.CreateTempSubdirectory("repo-checks-").FullName;
        internal FixtureRepository()
        {
            Write(".release-please-manifest.json", "{\".\":\"0.5.0\"}");
            Write("src/mssql-mcp/mssql-mcp.csproj", "<Project><VersionPrefix>0.5.0</VersionPrefix><Other>keep</Other></Project>\n");
            var dependencies = new JsonObject();
            foreach (var platform in Platforms)
            {
                dependencies[$"@codegiveness/mssql-mcp-{platform}"] = "0.5.0";
                WriteJson($"npm/platforms/{platform}/package.json", new JsonObject
                {
                    ["name"] = platform,
                    ["version"] = "0.5.0",
                    ["files"] = new JsonArray("mssql-mcp"),
                    ["cpu"] = new JsonArray("fixture-cpu")
                });
            }
            WriteJson("npm/package.json", new JsonObject { ["name"] = "fixture-main", ["version"] = "0.5.0", ["optionalDependencies"] = dependencies });
            WriteJson("server.json", new JsonObject
            {
                ["name"] = "fixture",
                ["version"] = "0.5.0",
                ["repository"] = new JsonObject { ["url"] = "https://example.test/repo" },
                ["packages"] = new JsonArray
                {
                    new JsonObject { ["registryType"] = "npm", ["version"] = "0.5.0", ["identifier"] = "fixture-npm", ["transport"] = new JsonObject { ["type"] = "stdio" } },
                    new JsonObject { ["registryType"] = "nuget", ["version"] = "0.5.0", ["identifier"] = "fixture-nuget", ["environmentVariables"] = new JsonArray(new JsonObject { ["name"] = "CONNECTION", ["isRequired"] = true }) }
                }
            });
        }
        internal string PathOf(string file) => Path.Combine(Root, file.Replace('/', Path.DirectorySeparatorChar));
        internal void Write(string file, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf(file))!);
            File.WriteAllText(PathOf(file), value);
        }
        internal void WriteJson(string file, JsonObject value) => Write(file, value.ToJsonString());
        internal JsonObject ReadJson(string file) => JsonNode.Parse(File.ReadAllText(PathOf(file)))!.AsObject();
        internal void Mutate(string file, Action<JsonObject> change)
        {
            var value = ReadJson(file);
            change(value);
            WriteJson(file, value);
        }
        internal void Drift(string stamp)
        {
            switch (stamp)
            {
                case "csproj": Write("src/mssql-mcp/mssql-mcp.csproj", "<Project><VersionPrefix>0.4.2</VersionPrefix></Project>"); break;
                case "package": Mutate("npm/package.json", obj => obj["version"] = "0.4.2"); break;
                case "server": Mutate("server.json", obj => obj["version"] = "0.4.2"); break;
                case "npm": Mutate("server.json", obj => obj["packages"]![0]!["version"] = "0.4.2"); break;
                case "nuget": Mutate("server.json", obj => obj["packages"]![1]!["version"] = "0.4.2"); break;
                default:
                    if (stamp.StartsWith("optional-", StringComparison.Ordinal))
                        Mutate("npm/package.json", obj => obj["optionalDependencies"]![$"@codegiveness/mssql-mcp-{stamp[9..]}"] = "0.4.2");
                    else Mutate($"npm/platforms/{stamp}/package.json", obj => obj["version"] = "0.4.2");
                    break;
            }
        }
        internal string[] StampBytes() => new[] { "src/mssql-mcp/mssql-mcp.csproj", "npm/package.json", "server.json" }
            .Concat(Platforms.Select(platform => $"npm/platforms/{platform}/package.json"))
            .Select(file => File.ReadAllText(PathOf(file))).ToArray();
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
