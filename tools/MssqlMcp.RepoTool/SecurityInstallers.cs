using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace MssqlMcp.RepoTool;

internal static partial class SecurityCommands
{
    private sealed record PinnedTool(string Version, string Url, string Sha256, string Member);
    // Upstream release asset metadata pins. Review URL and content together on updates.
    private static readonly Dictionary<string, PinnedTool> Tools = new(StringComparer.Ordinal)
    {
        ["gitleaks"] = new("8.30.1", "https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/gitleaks_8.30.1_linux_x64.tar.gz", "551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb", "gitleaks"),
        ["zizmor"] = new("1.31.1", "https://github.com/zizmorcore/zizmor/releases/download/v1.31.1/zizmor-x86_64-unknown-linux-gnu.tar.gz", "d185f726bdaf92df78273217dfdd24ebcd6b0ef48cf3ce776ff5762b6e138c99", "zizmor"),
        ["actionlint"] = new("1.7.12", "https://github.com/rhysd/actionlint/releases/download/v1.7.12/actionlint_1.7.12_linux_amd64.tar.gz", "8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8", "actionlint"),
        ["shellcheck"] = new("0.11.0", "https://github.com/koalaman/shellcheck/releases/download/v0.11.0/shellcheck-v0.11.0.linux.x86_64.tar.xz", "8c3be12b05d5c177a04c29e3c78ce89ac86f1595681cab149b65b97c4e227198", "shellcheck-v0.11.0/shellcheck"),
        ["trivy"] = new("0.76.1", "https://github.com/aquasecurity/trivy/releases/download/v0.76.1/trivy_0.76.1_Linux-64bit.tar.gz", "c7cbe3137fb8a48a17574bede3dd6beb16742d1e9772929940fc90082c39d9c0", "trivy")
    };
    private const string NpmCommit = "ae98c62586b6fea44114f72e249e246a2981de70"; // npm v12.2.1
    private const string NpmSourceHash = "51ebc8f7c8a612f036b2cbbe7c93bc6e00393cfb26fd23539befdcfe4e68022e";

    private static async Task DownloadAsync(string url, string path, string root)
    {
        var result = await ProcessRunner.RunAsync("curl", ["--fail", "--silent", "--show-error", "--location",
            "--proto", "=https", "--proto-redir", "=https", "--connect-timeout", "20", "--max-time", "180",
            "--retry", "2", "--output", path, url], root, timeoutSeconds: 600);
        if (result.ExitCode != 0) throw new SecurityPolicyException("Pinned release download failed");
    }

    private static async Task<int> InstallToolsAsync(string root, string[] args)
    {
        if (args.Length == 0 || args.Any(name => !Tools.ContainsKey(name)))
            throw new SecurityPolicyException("Usage: install-security-tools <gitleaks|zizmor|actionlint|shellcheck|trivy> [...]");
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new SecurityPolicyException("The pinned binaries support Linux x86-64 only");
        var destination = ToolsDirectory;
        Directory.CreateDirectory(destination);
        using var temporary = new TemporaryDirectory("security-tools-");
        foreach (var name in args)
        {
            var pin = Tools[name];
            var xz = pin.Url.EndsWith(".xz", StringComparison.Ordinal);
            var archive = Path.Combine(temporary.Path, xz ? "release.tar.xz" : "release.tar.gz");
            await DownloadAsync(pin.Url, archive, root);
            SecurityArchive.VerifySha256(archive, pin.Sha256, $"{name} {pin.Version}");
            if (xz)
            {
                var result = await ProcessRunner.RunAsync("xz", ["--decompress", "--keep", "--force", archive], root);
                if (result.ExitCode != 0) throw new SecurityPolicyException("Pinned archive decompression failed");
            }
            using (var stream = xz ? File.OpenRead(Path.Combine(temporary.Path, "release.tar")) : SecurityArchive.OpenGzip(archive))
                SecurityArchive.ExtractExecutable(stream, pin.Member, Path.Combine(temporary.Path, name));
            File.Move(Path.Combine(temporary.Path, name), Path.Combine(destination, name), overwrite: true);
            Console.WriteLine($"Installed {name} {pin.Version} (SHA-256 verified) in {destination}");
        }
        return 0;
    }

    internal static void ValidateNpmManifest(JsonObject upstream, JsonObject manifest)
    {
        foreach (var field in new[] { "version", "engines", "dependencies" })
            if (!upstream.ContainsKey(field) || !manifest.ContainsKey(field) || !JsonNode.DeepEquals(upstream[field], manifest[field]))
                throw new SecurityPolicyException($"npm runtime {field} does not match the pinned upstream source");
    }

    private static async Task<int> InstallNpmAsync(string root, string[] args)
    {
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache/mssql-mcp-npm");
        var print = false;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--print-cli") print = true;
            else if (args[index] == "--cache-dir" && index + 1 < args.Length) cache = args[++index];
            else throw new SecurityPolicyException("Usage: install-npm-cli [--cache-dir path] [--print-cli]");
        }
        var runtimeConfig = Path.Combine(root, ".config/npm-cli");
        var manifest = ReadObject(Path.Combine(runtimeConfig, "package.json"));
        var version = manifest["version"]?.GetValue<string>() ?? throw new SecurityPolicyException("Missing npm runtime version");
        var cacheRoot = Path.GetFullPath(cache);
        var destination = SecurityArchive.ContainedPath(cacheRoot, "npm-" + version);
        var cli = Path.Combine(destination, "npm/bin/npm-cli.js");
        if (print)
        {
            if (!File.Exists(cli)) throw new SecurityPolicyException("Install the content-locked npm CLI first");
            Console.WriteLine(cli);
            return 0;
        }
        if (destination.Contains('\n') || destination.Contains('\r'))
            throw new SecurityPolicyException("The npm cache path must not contain line breaks");
        Directory.CreateDirectory(cacheRoot);
        // Keep workspace/module symlinks and final rename on native cache storage, not HGFS.
        using (var staging = new TemporaryDirectory("npm-source-", cacheRoot))
        {
            var archive = Path.Combine(staging.Path, "source.tar.gz");
            await DownloadAsync($"https://codeload.github.com/npm/cli/tar.gz/{NpmCommit}", archive, root);
            SecurityArchive.VerifySha256(archive, NpmSourceHash, "npm source");
            var extracted = Path.Combine(staging.Path, "source");
            using (var stream = SecurityArchive.OpenGzip(archive)) SecurityArchive.ExtractSource(stream, extracted);
            var source = Path.Combine(staging.Path, "npm");
            Directory.Move(Path.Combine(extracted, "cli-" + NpmCommit), source);
            var upstream = ReadObject(Path.Combine(source, "package.json"));
            ValidateNpmManifest(upstream, manifest);
            var runtime = Path.Combine(staging.Path, "runtime");
            Directory.CreateDirectory(runtime);
            foreach (var name in new[] { "package.json", "package-lock.json" })
                File.Copy(Path.Combine(runtimeConfig, name), Path.Combine(runtime, name));
            Directory.Delete(Path.Combine(source, "node_modules"), recursive: true);
            var result = await ProcessRunner.RunAsync("npm", ["ci", "--prefix", runtime, "--ignore-scripts", "--omit=dev",
                "--no-audit", "--no-fund", "--bin-links=false", "--engine-strict",
                "--userconfig=" + (OperatingSystem.IsWindows() ? "NUL" : "/dev/null")], runtime, timeoutSeconds: 600);
            if (result.ExitCode != 0) throw new SecurityPolicyException("Integrity-locked npm runtime installation failed");
            var installation = Path.Combine(staging.Path, "installation");
            var packaged = Path.Combine(installation, "npm");
            Directory.CreateDirectory(packaged);
            var files = upstream["files"] as JsonArray ?? throw new SecurityPolicyException("Missing npm published file inventory");
            foreach (var name in files.Select(item => item!.GetValue<string>()).Concat(["package.json", "LICENSE", "README.md"]))
            {
                var item = SecurityArchive.ContainedPath(source, name);
                if (!File.Exists(item) && !Directory.Exists(item)) continue;
                var target = SecurityArchive.ContainedPath(packaged, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (Directory.Exists(item)) Directory.Move(item, target);
                else File.Move(item, target);
            }
            Directory.CreateSymbolicLink(Path.Combine(packaged, "node_modules"), "../runtime/node_modules");
            Directory.Move(runtime, Path.Combine(installation, "runtime"));
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            Directory.Move(installation, destination);
        }
        if (Environment.GetEnvironmentVariable("GITHUB_ENV") is { Length: > 0 } githubEnvironment)
            File.AppendAllText(githubEnvironment, $"NPM_CLI={cli}\n");
        Console.WriteLine($"Installed npm {version} (source SHA-256 and runtime SRI verified): {cli}");
        return 0;
    }
}
