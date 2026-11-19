using System.Text.RegularExpressions;

namespace MssqlMcp.RepoTool;

internal static partial class RepoContext
{
    internal static string DotNetExecutable => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    internal static string FindRoot(string start)
    {
        for (DirectoryInfo? directory = new(Path.GetFullPath(start)); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, ".release-please-manifest.json")))
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
        string path = Path.Join(root, ".env");
        if (!File.Exists(path))
        {
            return;
        }
        foreach (string line in File.ReadLines(path).Select(raw => raw.Trim()))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            string assignment = line.StartsWith("export ", StringComparison.Ordinal) ? line[7..].TrimStart() : line;
            int separator = assignment.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw new InvalidOperationException("Invalid .env assignment; shell expressions are not supported.");
            }
            string name = assignment[..separator].Trim();
            string value = assignment[(separator + 1)..].Trim();
            if (!EnvironmentName().IsMatch(name))
            {
                throw new InvalidOperationException("Invalid .env variable name.");
            }
            if (value.Length >= 2)
            {
                bool singleQuoted = value[0] == '\'' && value[^1] == '\'';
                bool doubleQuoted = value[0] == '"' && value[^1] == '"';
                if (singleQuoted || doubleQuoted)
                {
                    value = value[1..^1];
                }
            }
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentName();
}
