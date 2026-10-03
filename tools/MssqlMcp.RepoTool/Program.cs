using System.ComponentModel;
using System.Text.Json;
using System.Xml;

namespace MssqlMcp.RepoTool;

internal static class Program
{
    private const string Usage = "Usage: dotnet run --project tools/MssqlMcp.RepoTool -- [--root <directory>] <command> [arguments]\n" +
        "Commands: check-version-consistency, sync-all-stamps, check-release-policy, check-redistribution,\n" +
        "          lint-readme-snippets, coverage-summary, install-security-tools, install-npm-cli,\n" +
        "          security-scan, mcp-smoke, run-fuzz";

    internal static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }
        try
        {
            string root;
            if (args.Length >= 2 && args[0] == "--root")
            {
                root = Path.GetFullPath(args[1]);
                args = args[2..];
            }
            else
            {
                root = RepoContext.FindRoot(Environment.CurrentDirectory);
            }
            if (args.Length == 0)
            {
                throw new ArgumentException("A command is required.");
            }
            string command = args[0];
            string[] commandArgs = args[1..];
            return command switch
            {
                "check-version-consistency" or "sync-all-stamps" or "check-release-policy" or
                "check-redistribution" or "lint-readme-snippets" or "coverage-summary" =>
                    await RepositoryChecks.RunAsync(command, root, commandArgs).ConfigureAwait(false),
                "install-security-tools" or "install-npm-cli" or "security-scan" =>
                    await SecurityCommands.RunAsync(command, root, commandArgs).ConfigureAwait(false),
                "mcp-smoke" or "run-fuzz" =>
                    await RuntimeChecks.RunAsync(command, root, commandArgs).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unknown repository command '{command}'."),
            };
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or
            UnauthorizedAccessException or JsonException or XmlException or Win32Exception or TimeoutException)
        {
            // Input content and scanner diagnostics can contain credentials; never dump exceptions.
            Console.Error.WriteLine(error is ArgumentException or InvalidOperationException or TimeoutException
                ? $"Repository tool: {error.Message}" : $"Repository tool failed: {error.GetType().Name}.");
            return 1;
        }
    }
}
