using System.Text.RegularExpressions;

namespace MssqlMcp.RepoTool;

internal static partial class RepoContext
{
    internal static string DotNetExecutable => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    internal static string FindRoot(string start)
    {
        for (DirectoryInfo? directory = new(Path.GetFullPath(start)); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".release-please-manifest.json")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("Run the repository tool from the repository or supply --root <directory>.");
    }

    internal static void LoadDotEnv(string root)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING")))
        {
            return;
        }
        string path = Path.Combine(root, ".env");
        if (!File.Exists(path))
        {
            return;
        }
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line[7..].TrimStart();
            }
            int separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw new InvalidOperationException("Invalid .env assignment; shell expressions are not supported.");
            }
            string name = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (!EnvironmentName().IsMatch(name))
            {
                throw new InvalidOperationException("Invalid .env variable name.");
            }
            if (value.Length >= 2 && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
            {
                value = value[1..^1];
            }
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentName();
}
