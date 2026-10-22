using System.Security.Cryptography;

namespace MssqlMcp.RepoTool;

internal static partial class SecurityCommands
{
    private static string SyntheticRandom(int length)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return string.Create(length, alphabet, static (span, chars) =>
        {
            for (var index = 0; index < span.Length; index++) span[index] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        });
    }

    private static async Task DetectorGitAsync(string repository, params string[] arguments)
    {
        var result = await ScanProcessAsync("git", arguments, repository);
        if (result.ExitCode != 0) throw new SecurityPolicyException("Synthetic detector repository preparation failed");
    }

    private static void WriteFixture(string repository, string name, string content)
    {
        var path = Path.Combine(repository, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content + "\n");
    }

    private static async Task<bool> VerifyDetectorsAsync(string root, string reports)
    {
        // Generate synthetic credentials only in disposable repositories, never the worktree.
        var password = "Synthetic" + SyntheticRandom(24) + "!";
        var token = "ghp_" + SyntheticRandom(36);
        var passwordKey = "Pass" + "word";
        var pwdKey = "P" + "WD";
        var containerKey = "MSSQL_SA_" + "PASSWORD";
        var fixtureValue = "sec" + "ret";
        var containerValue = "YourStrong!" + "Passw0rd";
        var samples = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["plain.sql"] = $"Server=fixture.invalid;{passwordKey}={password};Database=example;",
            ["pwd.sql"] = $"{pwdKey}={password};Server=fixture.invalid;",
            ["quoted.sql"] = $"Server=fixture.invalid;{passwordKey}=\"{password};tail\";Database=example;",
            ["single.sql"] = $"Server=fixture.invalid;{passwordKey}='{password};tail';Database=example;",
            ["escaped.cs"] = $"Server=fixture.invalid;{passwordKey}=\\\"{password};tail\\\";Database=example;",
            ["braced.sql"] = $"Server=fixture.invalid;{passwordKey}={{{password};tail}};Database=example;",
            ["container.yml"] = $"{containerKey}: {password}",
            ["provider.txt"] = $"github_token = '{token}'",
            ["tests/new-consumer.cs"] = $"Server=fixture.invalid;{passwordKey}={fixtureValue};",
            ["tools/MssqlMcp.RepoTool/SecurityDetectors.cs"] = $"Server=fixture.invalid;{passwordKey}={password};",
            ["tests/mssql-mcp.Core.Tests/FileLoggerProviderTests.cs"] = $"Server=fixture.invalid;{passwordKey}={password};",
            [".github/workflows/ci.yml"] = $"{containerKey}: {password}"
        };
        using (var repository = new TemporaryDirectory("security-detectors-"))
        {
            await DetectorGitAsync(repository.Path, "init", "--quiet");
            await DetectorGitAsync(repository.Path, "config", "user.email", "fixture@example.invalid");
            await DetectorGitAsync(repository.Path, "config", "user.name", "Synthetic detector verification");
            foreach (var (filename, content) in samples) WriteFixture(repository.Path, filename, content);
            await DetectorGitAsync(repository.Path, "add", ".");
            await DetectorGitAsync(repository.Path, "commit", "--quiet", "-m", "Synthetic positives");
            await DetectorGitAsync(repository.Path, "rm", "-r", ".");
            await DetectorGitAsync(repository.Path, "commit", "--quiet", "-m", "Delete positives to verify history scanning");
            var (failed, findings) = await ScanSecretsAsync(root, repository.Path, Path.Combine(reports, "detector-positive.json"));
            var detected = findings.Select(finding => finding!["File"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var missing = samples.Keys.Except(detected).Order(StringComparer.Ordinal).ToArray();
            if (!failed || missing.Length != 0)
                throw new SecurityPolicyException("Detector missed positive fixture(s): " + string.Join(", ", missing));
            var serialized = findings.ToJsonString();
            if (serialized.Contains(password, StringComparison.Ordinal) || serialized.Contains(token, StringComparison.Ordinal))
                throw new SecurityPolicyException("Synthetic secret was not fully redacted");
        }
        using (var repository = new TemporaryDirectory("security-negative-"))
        {
            await DetectorGitAsync(repository.Path, "init", "--quiet");
            WriteFixture(repository.Path, "safe.sql", "Server=fixture.invalid;Integrated Security=true;\n"
                + $"Server=fixture.invalid;{passwordKey}=;\nServer=<your-server>;{passwordKey}=<your-password>;");
            WriteFixture(repository.Path, "tests/mssql-mcp.Core.Tests/FileLoggerProviderTests.cs",
                $"Server=fixture.invalid;{passwordKey}={fixtureValue};");
            WriteFixture(repository.Path, ".github/workflows/ci.yml", $"{containerKey}: {containerValue}");
            WriteFixture(repository.Path, "tools/MssqlMcp.RepoTool/SecurityDetectors.cs",
                $"var {passwordKey} = \"Synthetic\" + SyntheticRandom(24) + \"!\";");
            WriteFixture(repository.Path, ".gitignore", ".env");
            WriteFixture(repository.Path, ".env", $"Server=fixture.invalid;{passwordKey}={password};");
            await DetectorGitAsync(repository.Path, "add", ".");
            await DetectorGitAsync(repository.Path, "-c", "user.name=Synthetic detector verification", "-c",
                "user.email=fixture@example.invalid", "commit", "--quiet", "-m", "Synthetic negatives");
            var (failed, findings) = await ScanSecretsAsync(root, repository.Path, Path.Combine(reports, "detector-negative.json"));
            if (failed || findings.Count != 0)
                throw new SecurityPolicyException("Negative fixtures or ignored .env were incorrectly detected");
        }
        Console.WriteLine("Detector verification passed: provider/SQL positives, deleted history, redaction, scoped exceptions, negatives and ignored .env");
        return false;
    }
}
