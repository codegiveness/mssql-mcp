using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MssqlMcp.RepoTool;

internal static partial class SecurityCommands
{
    internal static async Task<int> RunAsync(string command, string root, string[] args)
    {
        try
        {
            return command switch
            {
                "install-security-tools" => await InstallToolsAsync(root, args),
                "install-npm-cli" => await InstallNpmAsync(root, args),
                "security-scan" => await ScanAsync(root, args),
                _ => throw new SecurityPolicyException("Unknown security command")
            };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Parser, process and filesystem exceptions can contain input credentials.
            var message = error is SecurityPolicyException ? error.Message : error.GetType().Name;
            Console.Error.WriteLine($"{(command == "security-scan" ? "Security scan" : "Security installation")} failed: {message}");
            return 1;
        }
    }

    internal static string ToolsDirectory => Path.GetFullPath(Environment.GetEnvironmentVariable("SECURITY_TOOLS_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache/mssql-mcp-security/bin"));

    private static string Tool(string name)
    {
        var executable = Path.Combine(ToolsDirectory, name);
        if (!File.Exists(executable))
            throw new SecurityPolicyException($"Install the pinned tool: install-security-tools {name}");
        return executable;
    }

    internal static Dictionary<string, string?> ScannerEnvironment(IEnumerable<KeyValuePair<string, string?>> environment)
    {
        var filtered = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in environment)
            if (pair.Key.StartsWith("GITLEAKS_", StringComparison.Ordinal)
                || pair.Key.StartsWith("TRIVY_", StringComparison.Ordinal)
                || pair.Key.StartsWith("ZIZMOR_", StringComparison.Ordinal))
                filtered[pair.Key] = null;
        return filtered;
    }

    private static Task<ProcessResult> ScanProcessAsync(string executable, IEnumerable<string> args, string root, string? input = null)
    {
        var environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Select(entry => new KeyValuePair<string, string?>((string)entry.Key, (string?)entry.Value));
        return ProcessRunner.RunAsync(executable, args, root, environment: ScannerEnvironment(environment), input: input);
    }

    private static void WriteJson(string path, JsonNode value) =>
        File.WriteAllText(path, value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");

    private static JsonObject ReadObject(string path) => JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new SecurityPolicyException("Expected a JSON object");

    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Path { get; }
        internal TemporaryDirectory(string prefix, string? parent = null)
        {
            Path = System.IO.Path.Combine(parent ?? System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

internal sealed class SecurityPolicyException(string message) : Exception(message)
{
}
