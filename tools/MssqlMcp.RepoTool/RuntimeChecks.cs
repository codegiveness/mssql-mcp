using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MssqlMcp.RepoTool;

internal static partial class RuntimeChecks
{
    private static readonly string[] ReadOnlyTools =
    [
        "list_databases", "list_schemas", "list_objects", "get_object_details",
        "explain_query", "analyze_indexes", "get_top_queries", "analyze_db_health",
    ];

    private const string BridgeUrl = "https://raw.githubusercontent.com/Metalnem/libfuzzer-dotnet/bd39d4e88d715ab460a929943645be2a186cde52/libfuzzer-dotnet.cc";
    private const string BridgeHash = "90f019e2e9ad3a0b93c7ecc2c5afb2fbfc8b5aab6aac51c7e0d349ec79354f36";

    internal static Task<int> RunAsync(string command, string root, string[] args) => command switch
    {
        "mcp-smoke" => SmokeAsync(root, args),
        "run-fuzz" => FuzzAsync(root, args),
        _ => throw new ArgumentException($"Unknown runtime command: {command}"),
    };

    private static async Task<int> SmokeAsync(string root, string[] args)
    {
        if (args.Length != 0)
        {
            throw new ArgumentException("Usage: mcp-smoke");
        }
        RepoContext.LoadDotEnv(root);
        string? connectionString = Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("mcp-smoke: MSSQL_CONNECTION_STRING not set and .env did not supply it.");
        }

        string binaryDirectory = Path.Combine(root, "src", "mssql-mcp", "bin", "Debug", "net10.0");
        string binary = Path.Combine(binaryDirectory, OperatingSystem.IsWindows() ? "mssql-mcp.exe" : "mssql-mcp");
        if (!File.Exists(binary))
        {
            Console.Error.WriteLine("mcp-smoke: binary not found, building...");
            await CheckedAsync(RepoContext.DotNetExecutable,
                ["build", Path.Combine(root, "src", "mssql-mcp"), "--nologo", "-v", "q"], root, "server build");
        }
        string serverCommand = binary;
        string[] serverArgs = [];
        if (!File.Exists(binary))
        {
            string assembly = Path.Combine(binaryDirectory, "mssql-mcp.dll");
            if (!File.Exists(assembly))
            {
                throw new InvalidOperationException("mcp-smoke: build did not produce a server executable or assembly.");
            }
            serverCommand = RepoContext.DotNetExecutable;
            serverArgs = [assembly];
        }

        string temporary = Directory.CreateTempSubdirectory("mssql-mcp-inspector-").FullName;
        try
        {
            string config = Path.Combine(temporary, "inspector.json");
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            await using (var stream = new FileStream(config, options))
            {
                await JsonSerializer.SerializeAsync(stream, new
                {
                    mcpServers = new Dictionary<string, object>
                    {
                        ["mssql-mcp"] = new
                        {
                            command = serverCommand,
                            args = serverArgs,
                            env = new { MSSQL_CONNECTION_STRING = connectionString },
                        },
                    },
                });
            }

            string inspector = Path.Combine(root, ".config", "npm-tools", "node_modules", "@modelcontextprotocol", "inspector", "clients", "launcher", "build", "index.js");
            if (!File.Exists(inspector))
            {
                string[] installArgs = ["ci", "--prefix", Path.Combine(root, ".config", "npm-tools"),
                    "--ignore-scripts", "--no-audit", "--no-fund", "--bin-links=false", "--engine-strict",
                    $"--userconfig={(OperatingSystem.IsWindows() ? "NUL" : "/dev/null")}"];
                string? npmCli = Environment.GetEnvironmentVariable("NPM_CLI");
                if (!string.IsNullOrEmpty(npmCli))
                {
                    await CheckedAsync("node", [npmCli, .. installArgs], root, "pinned Inspector installation");
                }
                else
                {
                    await CheckedAsync(OperatingSystem.IsWindows() ? "npm.cmd" : "npm", installArgs, root, "pinned Inspector installation");
                }
            }

            string[] inspectorArgs = [inspector, "--cli", "--config", config, "--server", "mssql-mcp",
                "--format", "json", "--stored-auth-only", "--connect-timeout", "15000"];
            var environment = new Dictionary<string, string?> { ["MCP_INSPECTOR_SECRET_STORE"] = "memory" };
            Console.WriteLine("=== [1] initialize + tools/list ===");
            ProcessResult tools = await ProcessRunner.RunAsync("node", [.. inspectorArgs, "--method", "tools/list"], root,
                timeoutSeconds: 60, environment: environment);
            ProcessRunner.RequireSuccess(tools, "tools/list Inspector invocation");
            using JsonDocument toolResponse = ParseResponse(tools.StandardOutput, "tools/list");
            JsonElement toolList = ReadToolList(toolResponse.RootElement);
            int failures = 0;
            if (toolList.GetArrayLength() == 9)
            {
                Console.WriteLine("[PASS] tools/list: 9 tools found");
            }
            else
            {
                Console.Error.WriteLine($"[FAIL] tools/list: expected 9 tools, got {toolList.GetArrayLength()}");
                failures++;
            }

            Console.WriteLine("=== [2] tools/call list_databases ===");
            ProcessResult databases = await ProcessRunner.RunAsync("node",
                [.. inspectorArgs, "--method", "tools/call", "--tool-name", "list_databases"], root,
                timeoutSeconds: 60, environment: environment);
            ProcessRunner.RequireSuccess(databases, "list_databases Inspector invocation");
            int databaseCount = ReadDatabaseCount(databases.StandardOutput);
            Console.WriteLine($"[PASS] list_databases: returned {databaseCount} databases");

            Console.WriteLine("=== [3] idempotentHint annotations ===");
            try
            {
                ValidateToolAnnotations(toolList);
                Console.WriteLine("[PASS] idempotentHint: 8 read-only=true, execute_sql=false");
            }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine($"[FAIL] idempotentHint: {error.Message}");
                failures++;
            }
            Console.WriteLine($"\n================================\n  PASSED: {3 - failures}  FAILED: {failures}\n================================");
            if (failures != 0)
            {
                return 1;
            }
            Console.WriteLine("ALL CHECKS PASSED");
            return 0;
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static JsonDocument ParseResponse(string json, string method)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // The raw Inspector response can contain connection details or database data.
            throw new InvalidOperationException($"{method}: failed to parse Inspector JSON response.");
        }
    }

    private static JsonElement ReadToolList(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("result", out JsonElement result)
            || result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("tools", out JsonElement tools)
            || tools.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("tools/list: expected a result.tools array.");
        }
        return tools;
    }

    internal static void ValidateToolsResponse(string json)
    {
        using JsonDocument response = ParseResponse(json, "tools/list");
        ValidateToolAnnotations(ReadToolList(response.RootElement));
    }

    private static void ValidateToolAnnotations(JsonElement tools)
    {
        var remaining = new HashSet<string>(ReadOnlyTools, StringComparer.Ordinal) { "execute_sql" };
        if (tools.GetArrayLength() != remaining.Count)
        {
            throw new InvalidOperationException("Expected exactly the nine server tools.");
        }
        foreach (JsonElement tool in tools.EnumerateArray())
        {
            if (tool.ValueKind != JsonValueKind.Object || !tool.TryGetProperty("name", out JsonElement name)
                || name.ValueKind != JsonValueKind.String || !remaining.Remove(name.GetString()!))
            {
                throw new InvalidOperationException("Tool names must match the nine unique expected server tools.");
            }
            bool expected = name.GetString() != "execute_sql";
            if (!tool.TryGetProperty("annotations", out JsonElement annotations)
                || annotations.ValueKind != JsonValueKind.Object
                || !annotations.TryGetProperty("idempotentHint", out JsonElement hint)
                || hint.ValueKind != (expected ? JsonValueKind.True : JsonValueKind.False))
            {
                throw new InvalidOperationException($"{name.GetString()}: expected idempotentHint={expected.ToString().ToLowerInvariant()}.");
            }
        }
    }

    internal static int ReadDatabaseCount(string json)
    {
        using JsonDocument response = ParseResponse(json, "list_databases");
        JsonElement root = response.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("result", out JsonElement result)
            || result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("list_databases: expected an Inspector result object.");
        }
        if (result.TryGetProperty("isError", out JsonElement isError) && isError.ValueKind != JsonValueKind.False)
        {
            throw new InvalidOperationException("list_databases: returned an error or invalid isError flag.");
        }
        if (!result.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("list_databases: expected text content.");
        }
        foreach (JsonElement block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("type", out JsonElement type)
                || type.ValueKind != JsonValueKind.String || type.GetString() != "text")
            {
                continue;
            }
            if (!block.TryGetProperty("text", out JsonElement text) || text.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("list_databases: text content is missing its text string.");
            }
            using JsonDocument databases = ParseResponse(text.GetString()!, "list_databases content");
            if (databases.RootElement.ValueKind != JsonValueKind.Array || databases.RootElement.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("list_databases: expected a nonempty database array.");
            }
            return databases.RootElement.GetArrayLength();
        }
        throw new InvalidOperationException("list_databases: no text content returned.");
    }

    internal static int ParseDuration(string value)
    {
        if (value.Length == 0 || value.Any(character => character is < '0' or > '9')
            || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
            || seconds is < 1 or > 1200)
        {
            throw new ArgumentException("Campaign duration must be between 1 and 1200 seconds.");
        }
        return seconds;
    }

    private static async Task<int> FuzzAsync(string root, string[] args)
    {
        if (args.Length > 3)
        {
            throw new ArgumentException("Usage: run-fuzz [output-directory] [seconds=60] [retained-corpus]");
        }
        int seconds = args.Length >= 2 ? ParseDuration(args[1]) : 60;
        string output = args.Length >= 1 && args[0].Length != 0
            ? Path.GetFullPath(args[0]) : Directory.CreateTempSubdirectory("mssql-mcp-fuzz-").FullName;
        PrepareOutput(output);
        Console.WriteLine($"Fuzz output: {output}");
        string corpus = Path.Combine(output, "corpus");
        foreach (string sample in Directory.EnumerateFiles(Path.Combine(root, "fuzz", "corpus"), "*.sql"))
        {
            File.Copy(sample, Path.Combine(corpus, Path.GetFileName(sample)), overwrite: false);
        }
        if (args.Length >= 3 && args[2].Length != 0)
        {
            ImportRetainedCorpus(args[2], corpus);
        }

        string project = Path.Combine(root, "fuzz", "mssql-mcp.Fuzz", "mssql-mcp.Fuzz.csproj");
        string target = Path.Combine(output, "target");
        string tools = Path.Combine(output, "tools");
        await CheckedAsync(RepoContext.DotNetExecutable, ["restore", project, "--locked-mode"], root, "fuzz restore");
        await CheckedAsync(RepoContext.DotNetExecutable,
            ["publish", project, "-c", "Release", "--no-restore", "-p:UseAppHost=false", "-o", target], root, "fuzz publish");
        await CheckedAsync(RepoContext.DotNetExecutable,
            ["tool", "install", "SharpFuzz.CommandLine", "--version", "2.3.0", "--tool-path", tools], root, "SharpFuzz installation");
        string sharpFuzz = Path.Combine(tools, OperatingSystem.IsWindows() ? "sharpfuzz.exe" : "sharpfuzz");
        string instrumentationLog = Path.Combine(output, "instrumentation.log");
        await LoggedAsync(sharpFuzz, [Path.Combine(target, "mssql-mcp.Core.dll"), "mssql_mcp.Core.Guard"],
            root, instrumentationLog, append: false);
        await LoggedAsync(sharpFuzz, [Path.Combine(target, "Microsoft.SqlServer.TransactSql.ScriptDom.dll")],
            root, instrumentationLog, append: true);

        string source = Path.Combine(output, "libfuzzer-dotnet.cc");
        await CheckedAsync("curl", ["--fail", "--silent", "--show-error", BridgeUrl, "--output", source], root, "native bridge download");
        await using (FileStream downloaded = File.OpenRead(source))
        {
            string digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(downloaded));
            if (digest != BridgeHash)
            {
                throw new InvalidOperationException("SHA-256 verification failed for the upstream native fuzz bridge.");
            }
        }
        string bridge = Path.Combine(output, OperatingSystem.IsWindows() ? "libfuzzer-dotnet.exe" : "libfuzzer-dotnet");
        await CheckedAsync("clang++", ["-fsanitize=fuzzer", source, "-o", bridge], root, "native bridge compilation");
        string[] targetArgs = [$"--target_path={RepoContext.DotNetExecutable}", $"--target_arg={Path.Combine(target, "mssql-mcp.Fuzz.dll")}"];
        string probe = Path.Combine(output, "probe");
        string probeLog = Path.Combine(output, "crash-probe.log");
        ProcessResult probeResult = await ProcessRunner.RunAsync(bridge,
            [.. targetArgs, "-runs=1", "-max_len=4096", $"-artifact_prefix={probe}{Path.DirectorySeparatorChar}"], root,
            timeoutSeconds: 20, environment: new Dictionary<string, string?> { ["MSSQL_FUZZ_CRASH_PROBE"] = "1" });
        await SaveLogAsync(probeResult, probeLog, append: false);
        ValidateCrashProbe(probeResult.ExitCode, probe, probeLog);

        // Keep all real findings and the campaign exit status, including deadline failure.
        ProcessResult campaign = await ProcessRunner.RunAsync(bridge,
            [.. targetArgs, "-seed=1", "-max_len=4096", "-timeout=5", $"-max_total_time={seconds}",
                $"-dict={Path.Combine(root, "fuzz", "sql.dict")}",
                $"-artifact_prefix={Path.Combine(output, "findings")}{Path.DirectorySeparatorChar}", corpus], root,
            timeoutSeconds: seconds + 20, environment: new Dictionary<string, string?> { ["MSSQL_FUZZ_CRASH_PROBE"] = null });
        await SaveLogAsync(campaign, Path.Combine(output, "campaign.log"), append: false);
        return campaign.ExitCode;
    }

    internal static void PrepareOutput(string output)
    {
        if (IsLink(output))
        {
            throw new InvalidOperationException("The fuzz output directory must not be a symbolic link.");
        }
        Directory.CreateDirectory(output);
        if (Directory.EnumerateFileSystemEntries(output).Any())
        {
            throw new InvalidOperationException("Use a fresh output directory; previous corpora and findings are never overwritten.");
        }
        foreach (string name in new[] { "corpus", "probe", "findings" })
        {
            Directory.CreateDirectory(Path.Combine(output, name));
        }
    }

    internal static void ImportRetainedCorpus(string retained, string corpus)
    {
        if (!Directory.Exists(retained) || HasLinkedAncestor(retained))
        {
            return;
        }
        byte[] buffer = new byte[4097];
        Span<byte> digest = stackalloc byte[32];
        foreach (string sample in Directory.EnumerateFiles(retained))
        {
            if (IsLink(sample) || new FileInfo(sample).Length > 4096)
            {
                continue;
            }
            // O_NONBLOCK prevents a retained FIFO from hanging the command; O_NOFOLLOW
            // prevents a sample replaced by a symlink from being read after inspection.
            using FileStream stream = OpenRetainedSample(sample);
            if (!stream.CanSeek || stream.Length > 4096)
            {
                continue;
            }
            // Read a bounded snapshot: a growing file must not bypass the corpus limit.
            int count = 0;
            while (count < buffer.Length)
            {
                int read = stream.Read(buffer, count, buffer.Length - count);
                if (read == 0)
                {
                    break;
                }
                count += read;
            }
            if (count > 4096)
            {
                continue;
            }
            ReadOnlySpan<byte> data = buffer.AsSpan(0, count);
            SHA256.HashData(data, digest);
            string destination = Path.Combine(corpus, Convert.ToHexStringLower(digest));
            if (!File.Exists(destination))
            {
                using var copied = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                copied.Write(data);
            }
        }
    }

    private static FileStream OpenRetainedSample(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return File.OpenRead(path);
        }
        int flags = OperatingSystem.IsMacOS() ? 0x104 : 0x20800; // O_NOFOLLOW | O_NONBLOCK
        int descriptor = OpenUnix(path, flags);
        if (descriptor < 0)
        {
            throw new IOException("Unable to safely open a retained corpus sample.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        }
        var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        try
        {
            return new FileStream(handle, FileAccess.Read);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenUnix(string path, int flags);

    private static bool HasLinkedAncestor(string path)
    {
        for (DirectoryInfo? directory = new DirectoryInfo(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
        {
            if (IsLink(directory.FullName))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    internal static void ValidateCrashProbe(int exitCode, string probeDirectory, string log)
    {
        if (exitCode == 0 || !Directory.EnumerateFiles(probeDirectory, "crash-*").Any(file => !IsLink(file))
            || !File.ReadAllText(log).Contains("Intentional fuzz-engine crash-detection probe", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The intentional crash probe did not produce the expected crash evidence.");
        }
    }

    private static async Task CheckedAsync(string executable, string[] args, string root, string description)
    {
        ProcessResult result = await ProcessRunner.RunAsync(executable, args, root);
        Console.Write(result.StandardOutput);
        Console.Error.Write(result.StandardError);
        ProcessRunner.RequireSuccess(result, description);
    }

    private static async Task LoggedAsync(string executable, string[] args, string root, string log, bool append)
    {
        ProcessResult result = await ProcessRunner.RunAsync(executable, args, root);
        await SaveLogAsync(result, log, append);
        ProcessRunner.RequireSuccess(result, "managed fuzz instrumentation");
    }

    private static async Task SaveLogAsync(ProcessResult result, string log, bool append)
    {
        Console.Write(result.StandardOutput);
        Console.Error.Write(result.StandardError);
        if (!append)
        {
            await File.WriteAllTextAsync(log, result.StandardOutput, Encoding.UTF8);
        }
        else
        {
            await File.AppendAllTextAsync(log, result.StandardOutput, Encoding.UTF8);
        }
        await File.AppendAllTextAsync(log, result.StandardError, Encoding.UTF8);
    }
}
