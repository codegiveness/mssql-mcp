using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MssqlMcp.RepoTool;

internal static partial class RepositoryChecks
{
    private static readonly (string Source, string Type)[] CriticalSources =
    [
        ("src/mssql-mcp.Core/Guard/SqlGuard.cs", "mssql_mcp.Core.Guard.SqlGuard"),
        ("src/mssql-mcp.Core/Logging/PasswordObfuscator.cs", "mssql_mcp.Core.Logging.PasswordObfuscator"),
        ("src/mssql-mcp.Core/SqlQueryResult.cs", "mssql_mcp.Core.ResultByteBudget"),
        ("src/mssql-mcp.Core/SqlExecutor.cs", "mssql_mcp.Core.SqlExecutor"),
        ("src/mssql-mcp.Tools/ToolErrors.cs", "mssql_mcp.Tools.ToolErrors")
    ];
    private static readonly Regex JsonFence = new("```jsonc?\\r?\\n([\\s\\S]*?)```");
    private static readonly Regex BadgeImage = new(@"!\[[^\]]*\]\(([^)]+)\)");

    private readonly record struct CoverageCounts(long CoveredLines, long CoverableLines, long CoveredBranches, long TotalBranches)
    {
        public static CoverageCounts operator +(CoverageCounts a, CoverageCounts b) => new(
            checked(a.CoveredLines + b.CoveredLines), checked(a.CoverableLines + b.CoverableLines),
            checked(a.CoveredBranches + b.CoveredBranches), checked(a.TotalBranches + b.TotalBranches));
    }

    private static CoverageCounts Counts(JsonNode? metric)
    {
        if (metric is not JsonObject value) throw new InvalidOperationException("Missing coverage counts.");
        return ValidateCounts(new CoverageCounts(Count(value["coveredlines"], "coveredlines"), Count(value["coverablelines"], "coverablelines"),
            Count(value["coveredbranches"], "coveredbranches"), Count(value["totalbranches"], "totalbranches")));
    }

    private static long Count(JsonNode? node, string name)
    {
        if (node is not JsonValue number)
            throw new InvalidOperationException($"Missing or invalid coverage count: {name}");
        var count = number.TryGetValue<int>(out var small) ? small :
            number.TryGetValue<long>(out var integer) ? integer :
            number.TryGetValue<double>(out var real) ? real : double.NaN;
        if (!double.IsFinite(count) || count < 0 || count > 9007199254740991 || count != Math.Truncate(count))
            throw new InvalidOperationException($"Missing or invalid coverage count: {name}");
        return (long)count;
    }

    private static CoverageCounts ValidateCounts(CoverageCounts result)
    {
        if (result.CoveredLines > result.CoverableLines || result.CoveredBranches > result.TotalBranches)
            throw new InvalidOperationException("Covered counts exceed coverable counts.");
        return result;
    }

    private static void RequireInstrumentation(CoverageCounts counts, string source)
    {
        if (counts.CoverableLines == 0 || counts.TotalBranches == 0)
            throw new InvalidOperationException($"{source} has no line/branch instrumentation.");
    }

    private static string Ratio(long covered, long total) => total == 0 ? "N/A (0/0)" :
        $"{Math.Round(100.0 * covered / total, 1, MidpointRounding.AwayFromZero).ToString("F1", CultureInfo.InvariantCulture)}% ({covered}/{total})";
    private static string CoverageRow(string label, CoverageCounts counts) =>
        $"| {label} | {Ratio(counts.CoveredLines, counts.CoverableLines)} | {Ratio(counts.CoveredBranches, counts.TotalBranches)} |";

    internal static string SummarizeCoverage(JsonObject report, JsonObject npmCoverage)
    {
        var overall = Counts(report["summary"]);
        RequireInstrumentation(overall, "Production report");
        if (report["coverage"] is not JsonObject coverage || coverage["assemblies"] is not JsonArray assemblies)
            throw new InvalidOperationException("Missing production coverage assemblies.");
        foreach (var name in new[] { "mssql-mcp.Core", "mssql-mcp.Tools" })
            if (!assemblies.OfType<JsonObject>().Any(assembly => StringValue(assembly["name"]) == name))
                throw new InvalidOperationException($"Missing production assembly: {name}");
        var classes = new List<JsonObject>();
        foreach (var assembly in assemblies)
        {
            if (assembly is not JsonObject obj || obj["classesinassembly"] is not JsonArray items)
                throw new InvalidOperationException("Missing assembly classes.");
            foreach (var item in items)
                classes.Add(item as JsonObject ?? throw new InvalidOperationException("Invalid coverage class."));
        }
        var rows = new List<string> { CoverageRow("Production Core + Tools", overall) };
        foreach (var (source, type) in CriticalSources)
        {
            var found = false;
            var total = new CoverageCounts();
            foreach (var item in classes)
            {
                var name = StringValue(item["name"]);
                if (name != type && !(name?.StartsWith(type + "+", StringComparison.Ordinal) == true ||
                    name?.StartsWith(type + "/", StringComparison.Ordinal) == true || name?.StartsWith(type + ".", StringComparison.Ordinal) == true)) continue;
                found = true;
                total += Counts(item);
            }
            if (!found) throw new InvalidOperationException($"Critical source is absent from coverage: {source}");
            RequireInstrumentation(total, source);
            rows.Add(CoverageRow($"`{source}`", total));
        }
        var npmSource = npmCoverage.FirstOrDefault(entry => entry.Key.Replace('\\', '/').EndsWith("/npm/bin/mssql-mcp.js", StringComparison.Ordinal));
        if (npmSource.Value is not JsonObject metric || metric["lines"] is not JsonObject lines || metric["branches"] is not JsonObject branches)
            throw new InvalidOperationException("npm download/cache source is absent from coverage.");
        var npmCounts = ValidateCounts(new CoverageCounts(Count(lines["covered"], "coveredlines"), Count(lines["total"], "coverablelines"),
            Count(branches["covered"], "coveredbranches"), Count(branches["total"], "totalbranches")));
        RequireInstrumentation(npmCounts, "npm download/cache source");
        rows.Add(CoverageRow("`npm/bin/mssql-mcp.js` (download/cache/archive boundaries)", npmCounts));
        return "## Targeted security coverage\n\n" +
            "Measured from live SQL tests and native V8 npm security regressions, not a coverage threshold. Counts show covered/coverable items.\n\n" +
            "| Production source | Line coverage | Branch coverage |\n| --- | --- | --- |\n" + string.Join('\n', rows) + "\n\n" +
            "The uploaded coverage artifact includes Cobertura, raw collector data, and HTML source reports with uncovered lines/branches.\n";
    }

    private static void WriteCoverageSummary(string summaryPath, string npmPath)
    {
        var markdown = SummarizeCoverage(ReadObject(summaryPath), ReadObject(npmPath));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(summaryPath)!, "critical-summary.md"), markdown);
        var githubSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrEmpty(githubSummary)) File.AppendAllText(githubSummary, markdown);
        Console.Write(markdown);
    }

    internal sealed record ReadmeLint(int BlockCount, IReadOnlyList<string> Errors);

    internal static ReadmeLint LintReadme(string readme)
    {
        var errors = new List<string>();
        var blocks = JsonFence.Matches(readme);
        for (var index = 0; index < blocks.Count; index++)
        {
            void Fail(string error) => errors.Add($"FAIL - README JSON block #{index + 1}: {error}");
            JsonNode? json;
            try
            {
                json = JsonNode.Parse(blocks[index].Groups[1].Value, documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            }
            catch (JsonException error) { Fail($"invalid JSON: {error.Message}"); continue; }
            if (json is not JsonObject obj) continue;
            var config = obj["mcpServers"] is JsonObject or JsonArray ? obj["mcpServers"] : obj["mcp"];
            IEnumerable<KeyValuePair<string, JsonNode?>> entries = config switch
            {
                JsonObject servers => servers,
                JsonArray servers => servers.Select((server, position) => new KeyValuePair<string, JsonNode?>(position.ToString(CultureInfo.InvariantCulture), server)),
                _ => []
            };
            foreach (var (name, node) in entries)
            {
                if (node is not JsonObject server) { Fail($"server \"{name}\" is not a configuration object"); continue; }
                if (!Truthy(server["command"])) Fail($"server \"{name}\" missing \"command\"");
                if (!Truthy(server["args"]) && !Truthy(server["env"])) Fail($"server \"{name}\" needs \"args\" or \"env\"");
            }
        }
        foreach (Match badge in BadgeImage.Matches(readme))
        {
            var url = badge.Groups[1].Value;
            if (!url.StartsWith("http://", StringComparison.Ordinal) && !url.StartsWith("https://", StringComparison.Ordinal))
                errors.Add($"FAIL - README badge image URL is malformed: {url}");
        }
        return new ReadmeLint(blocks.Count, errors);
    }

    private static bool Truthy(JsonNode? node)
    {
        if (node is null) return false;
        if (node is not JsonValue value) return true;
        if (value.TryGetValue<string>(out var text)) return text.Length != 0;
        if (value.TryGetValue<bool>(out var boolean)) return boolean;
        if (value.TryGetValue<double>(out var number)) return number != 0;
        return true;
    }
}
