using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MssqlMcp.RepoTool;

internal static partial class RepositoryChecks
{
    private static readonly string[] Platforms = ["linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64"];
    private static readonly Regex VersionPrefix = new(@"<VersionPrefix>\s*([^<\s]+)\s*</VersionPrefix>");
    private static readonly Regex ReleaseTag = new(@"\Av((?:0|[1-9][0-9]*))\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z");
    private static readonly JsonSerializerOptions JsonOutput = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static Task<int> RunAsync(string command, string root, string[] args)
    {
        try
        {
            switch (command)
            {
                case "check-version-consistency":
                    RequireArguments(args, 0, command);
                    var errors = CheckVersionConsistency(root);
                    if (errors.Count != 0)
                    {
                        foreach (var error in errors) Console.Error.WriteLine(error);
                        return Task.FromResult(1);
                    }
                    Console.WriteLine("Version consistency: all stamps match.");
                    break;
                case "sync-all-stamps":
                    RequireArguments(args, 0, command);
                    var sync = SyncAllStamps(root);
                    Console.WriteLine(sync.ChangedFiles.Count == 0 ? $"All stamps already at version {sync.Version}" : $"Synced stamps to version {sync.Version}:");
                    foreach (var file in sync.ChangedFiles) Console.WriteLine($"  {file} updated");
                    break;
                case "check-release-policy":
                    var environment = new Dictionary<string, string?>();
                    foreach (var key in new[] { "GITHUB_EVENT_NAME", "GITHUB_REF_TYPE", "GITHUB_REF_NAME", "RELEASE_TAG", "GITHUB_OUTPUT" })
                        environment[key] = Environment.GetEnvironmentVariable(key);
                    var release = CheckReleasePolicy(root, args, environment);
                    Console.WriteLine($"Release policy: accepted {release.Name}.");
                    break;
                case "check-redistribution":
                    if (args.Length > 1) throw new InvalidOperationException("Usage: check-redistribution [lock-path]");
                    CheckRedistribution(args.Length == 1 ? ResolvePath(root, args[0]) : Path.Combine(root, "src", "mssql-mcp", "packages.lock.json"));
                    Console.WriteLine("Redistribution policy: known native broker hold not present.");
                    break;
                case "coverage-summary":
                    RequireArguments(args, 2, command);
                    WriteCoverageSummary(ResolvePath(root, args[0]), ResolvePath(root, args[1]));
                    break;
                case "lint-readme-snippets":
                    RequireArguments(args, 0, command);
                    var readme = LintReadme(File.ReadAllText(Path.Combine(root, "README.md")));
                    if (readme.Errors.Count != 0)
                    {
                        foreach (var error in readme.Errors) Console.Error.WriteLine(error);
                        return Task.FromResult(1);
                    }
                    Console.WriteLine($"All {readme.BlockCount} README JSON snippet(s) valid.");
                    Console.WriteLine("All README badge image URLs are well-formed.");
                    break;
                default:
                    throw new InvalidOperationException($"Unknown repository check: {command}");
            }
            return Task.FromResult(0);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"{command}: {error.Message}");
            return Task.FromResult(1);
        }
    }

    private static void RequireArguments(string[] args, int count, string command)
    {
        if (args.Length != count) throw new InvalidOperationException($"Invalid arguments for {command}.");
    }

    private static string ResolvePath(string root, string path) => Path.GetFullPath(path, root);
    private static string? StringValue(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static JsonObject ReadObject(string path) => JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidOperationException($"Expected a JSON object in {path}.");
    private static string ManifestVersion(string root)
    {
        var path = Path.Combine(root, ".release-please-manifest.json");
        var version = StringValue(ReadObject(path)["."]);
        return !string.IsNullOrEmpty(version) ? version : throw new InvalidOperationException($"manifest at {path} has no \".\" version string");
    }

    private static IEnumerable<(string Label, string Path)> PackagePaths(string root)
    {
        yield return ("package.json", Path.Combine(root, "npm", "package.json"));
        foreach (var platform in Platforms)
            yield return ($"platforms/{platform}/package.json", Path.Combine(root, "npm", "platforms", platform, "package.json"));
    }

    internal static IReadOnlyList<string> CheckVersionConsistency(string root)
    {
        var errors = new List<string>();
        string version;
        try { version = ManifestVersion(root); }
        catch (Exception error) when (IsFileError(error)) { errors.Add($"manifest: {error.Message}"); return errors; }

        void Inspect(string label, Action action)
        {
            try { action(); }
            catch (Exception error) when (IsFileError(error)) { errors.Add($"{label}: {error.Message}"); }
        }
        void Check(string label, JsonNode? node)
        {
            var actual = StringValue(node);
            if (actual != version) errors.Add($"{label}: version is {actual ?? "missing"}, expected {version}");
        }

        Inspect("csproj", () =>
        {
            var match = VersionPrefix.Match(File.ReadAllText(Path.Combine(root, "src", "mssql-mcp", "mssql-mcp.csproj")));
            if (!match.Success) errors.Add("csproj: <VersionPrefix> not found");
            else if (match.Groups[1].Value != version) errors.Add($"csproj: <VersionPrefix> is {match.Groups[1].Value}, expected {version}");
        });
        foreach (var (label, path) in PackagePaths(root))
        {
            Inspect(label, () =>
            {
                var package = ReadObject(path);
                Check(label, package["version"]);
                if (label == "package.json" && package["optionalDependencies"] is JsonObject dependencies)
                    foreach (var dependency in dependencies) Check($"{label}: optionalDependency {dependency.Key}", dependency.Value);
            });
        }
        Inspect("server.json", () =>
        {
            var server = ReadObject(Path.Combine(root, "server.json"));
            Check("server.json: top-level", server["version"]);
            if (server["packages"] is not JsonArray packages) { errors.Add("server.json: missing packages array"); return; }
            foreach (var item in packages)
            {
                if (item is not JsonObject package) { errors.Add("server.json: invalid package"); continue; }
                Check($"server.json: {StringValue(package["registryType"]) ?? "unknown"} package", package["version"]);
            }
        });
        return errors;
    }

    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException;

    internal sealed record StampSync(string Version, IReadOnlyList<string> ChangedFiles);

    internal static StampSync SyncAllStamps(string root)
    {
        var version = ManifestVersion(root);
        var changed = new List<string>();
        var csprojPath = Path.Combine(root, "src", "mssql-mcp", "mssql-mcp.csproj");
        var xml = File.ReadAllText(csprojPath);
        var match = VersionPrefix.Match(xml);
        if (!match.Success) throw new InvalidOperationException($"no <VersionPrefix> found in {csprojPath}");
        if (match.Groups[1].Value != version)
        {
            File.WriteAllText(csprojPath, VersionPrefix.Replace(xml, _ => $"<VersionPrefix>{version}</VersionPrefix>", 1));
            changed.Add("csproj");
        }
        foreach (var (label, path) in PackagePaths(root))
        {
            var package = ReadObject(path);
            var touched = SetVersion(package, version);
            if (package["optionalDependencies"] is JsonObject dependencies)
                foreach (var key in dependencies.Select(item => item.Key).ToArray())
                    if (StringValue(dependencies[key]) != version) { dependencies[key] = version; touched = true; }
            if (touched) { WriteObject(path, package); changed.Add(label); }
        }
        var serverPath = Path.Combine(root, "server.json");
        var server = ReadObject(serverPath);
        if (server["packages"] is not JsonArray packages || packages.Count < 2 || packages[0] is not JsonObject npm || packages[1] is not JsonObject nuget)
            throw new InvalidOperationException("server.json must have packages[0] (npm) and packages[1] (nuget)");
        var serverTouched = SetVersion(server, version);
        serverTouched |= SetVersion(npm, version);
        serverTouched |= SetVersion(nuget, version);
        if (serverTouched) { WriteObject(serverPath, server); changed.Add("server.json"); }
        return new StampSync(version, changed);
    }

    private static bool SetVersion(JsonObject value, string version)
    {
        if (StringValue(value["version"]) == version) return false;
        value["version"] = version;
        return true;
    }
    private static void WriteObject(string path, JsonObject value) => File.WriteAllText(path, value.ToJsonString(JsonOutput) + "\n");

    internal sealed record Release(string Name, string Version);
    internal static Release ValidateReleaseTag(string? tag)
    {
        var match = tag is null ? Match.Empty : ReleaseTag.Match(tag);
        if (!match.Success) throw new InvalidOperationException("Release tag must be v-prefixed SemVer (for example v0.5.5).");
        if (match.Groups[1].Value != "0") throw new InvalidOperationException("Release policy permits only 0.x versions; major versions >= 1 are blocked.");
        return new Release(tag!, tag![1..]);
    }

    internal static Release CheckReleasePolicy(string root, string[] args, IReadOnlyDictionary<string, string?> environment)
    {
        string? Env(string name) => environment.TryGetValue(name, out var value) ? value : null;
        Release release;
        if (args.Length is 1 or 2 && args[0] == "--manifest")
        {
            var manifest = ReadObject(args.Length == 2 ? ResolvePath(root, args[1]) : Path.Combine(root, ".release-please-manifest.json"));
            var version = StringValue(manifest["."]);
            release = ValidateReleaseTag(version is null ? null : "v" + version);
        }
        else if (args.Length == 1 && args[0] == "--github-event")
        {
            release = Env("GITHUB_EVENT_NAME") switch
            {
                "workflow_dispatch" => ValidateReleaseTag(Env("RELEASE_TAG")),
                "push" when Env("GITHUB_REF_TYPE") == "tag" => ValidateReleaseTag(Env("GITHUB_REF_NAME")),
                _ => throw new InvalidOperationException("Release requires a tag push or workflow_dispatch with a release tag.")
            };
        }
        else if (args.Length == 1) release = ValidateReleaseTag(args[0]);
        else throw new InvalidOperationException("Usage: check-release-policy <tag> | --manifest [path] | --github-event");
        if (!string.IsNullOrEmpty(Env("GITHUB_OUTPUT")))
            File.AppendAllText(Env("GITHUB_OUTPUT")!, $"name={release.Name}\nversion={release.Version}\n");
        return release;
    }

    internal static void CheckRedistribution(string path)
    {
        var inventory = ReadObject(path);
        if (inventory["dependencies"] is not JsonObject dependencies || dependencies.Count == 0)
            throw new InvalidOperationException("Dependency inventory is invalid or empty; cannot assess the publication hold.");
        foreach (var entry in dependencies)
        {
            if (entry.Value is not JsonObject graph || graph.Count == 0)
                throw new InvalidOperationException("Dependency graph is invalid or empty; cannot assess the publication hold.");
            if (graph.TryGetPropertyValue("Microsoft.Identity.Client.NativeInterop", out var broker))
            {
                var version = broker is JsonObject package ? StringValue(package["resolved"]) : null;
                throw new InvalidOperationException($"Public distribution blocked: Microsoft.Identity.Client.NativeInterop {version ?? "(unknown version)"} has not been cleared for redistribution. Its packaged license section 3(e) prohibits distribution. Owner licensing review is required.");
            }
        }
    }
}
