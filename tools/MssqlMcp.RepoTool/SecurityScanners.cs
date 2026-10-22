using System.Text.Json.Nodes;

namespace MssqlMcp.RepoTool;

internal static partial class SecurityCommands
{
    private static readonly string[] FindingMetadata = ["RuleID", "File", "StartLine", "EndLine", "StartColumn", "EndColumn", "Commit", "Fingerprint"];

    internal static JsonArray SanitizeFindings(JsonArray findings)
    {
        var safe = new JsonArray();
        foreach (var entry in findings)
        {
            if (entry is not JsonObject finding) throw new SecurityPolicyException("Invalid Gitleaks finding");
            var metadata = new JsonObject { ["Secret"] = "REDACTED" };
            foreach (var key in FindingMetadata)
                if (finding.TryGetPropertyValue(key, out var value)) metadata[key] = value?.DeepClone();
            safe.Add(metadata);
        }
        return safe;
    }

    internal static void RequireRedactedFindings(JsonArray findings)
    {
        if (findings.Any(entry => entry is not JsonObject finding || finding["Secret"]?.GetValue<string>() != "REDACTED"))
            throw new SecurityPolicyException("Gitleaks did not redact every finding");
    }

    private static async Task<(bool Failed, JsonArray Findings)> ScanSecretsAsync(string root, string repository, string report)
    {
        var shallow = await ScanProcessAsync("git", ["rev-parse", "--is-shallow-repository"], repository);
        if (shallow.ExitCode != 0 || shallow.StandardOutput.Trim() != "false")
            throw new SecurityPolicyException("Full-history scanning requires a non-shallow Git repository (fetch-depth: 0)");
        using var temporary = new TemporaryDirectory("gitleaks-report-");
        var raw = Path.Combine(temporary.Path, "findings.json");
        var result = await ScanProcessAsync(Tool("gitleaks"), ["git", repository, "--config", Path.Combine(root, ".gitleaks.toml"),
            "--log-opts=--all --full-history --diff-merges=separate", "--redact=100", "--ignore-gitleaks-allow",
            "--gitleaks-ignore-path", temporary.Path, "--exit-code", "23", "--timeout", "600", "--no-banner", "--no-color",
            "--report-format", "json", "--report-path", raw], root);
        // Raw output, parser errors, matches and commit messages must never reach logs.
        var findings = File.Exists(raw) ? JsonNode.Parse(File.ReadAllText(raw)) as JsonArray
            ?? throw new SecurityPolicyException("Invalid Gitleaks report") : new JsonArray();
        var safe = SanitizeFindings(findings);
        WriteJson(report, safe);
        if (result.ExitCode is not (0 or 23))
            throw new SecurityPolicyException($"Gitleaks failed (exit {result.ExitCode}); raw output withheld to protect credentials");
        if (!File.Exists(raw)) throw new SecurityPolicyException("Gitleaks did not produce its required report");
        RequireRedactedFindings(findings);
        Console.WriteLine($"Gitleaks: {safe.Count} redacted finding(s); metadata report: {report}");
        foreach (var finding in safe)
            Console.WriteLine($"  {finding!["RuleID"]}: {finding["File"]}:{finding["StartLine"]} commit {finding["Commit"]}");
        return (result.ExitCode != 0, findings);
    }

    private static async Task<bool> WorkflowAuditAsync(string root, string reports)
    {
        var directory = Path.Combine(root, ".github/workflows");
        var workflows = Directory.GetFiles(directory, "*.yml").Order(StringComparer.Ordinal)
            .Concat(Directory.GetFiles(directory, "*.yaml").Order(StringComparer.Ordinal)).ToArray();
        if (workflows.Length == 0) throw new SecurityPolicyException("No workflows found");
        var shellcheck = Path.Combine(ToolsDirectory, "shellcheck");
        var actionlint = await ScanProcessAsync(Tool("actionlint"),
            new[] { "-shellcheck", File.Exists(shellcheck) ? shellcheck : "shellcheck", "-format", "{{json .}}" }.Concat(workflows), root);
        File.WriteAllText(Path.Combine(reports, "actionlint.json"), actionlint.StandardOutput);
        File.WriteAllText(Path.Combine(reports, "actionlint.log"), actionlint.StandardError);
        // Offline JSON analysis retains finding exit codes and needs no fork-PR token.
        var zizmor = await ScanProcessAsync(Tool("zizmor"),
            new[] { "--offline", "--strict-collection", "--persona=regular", "--format=json", "--color=never" }.Concat(workflows), root);
        File.WriteAllText(Path.Combine(reports, "zizmor.json"), zizmor.StandardOutput);
        File.WriteAllText(Path.Combine(reports, "zizmor.log"), zizmor.StandardError);
        Console.WriteLine(actionlint.StandardOutput);
        Console.WriteLine(actionlint.StandardError);
        Console.WriteLine(zizmor.StandardOutput);
        Console.WriteLine(zizmor.StandardError);
        Console.WriteLine($"actionlint exit: {actionlint.ExitCode}; zizmor exit: {zizmor.ExitCode}");
        return actionlint.ExitCode != 0 || zizmor.ExitCode != 0;
    }

    internal static (bool Failed, int Packages, int Vulnerabilities) AssessVulnerabilities(int exitCode, JsonObject document)
    {
        var results = document["Results"] as JsonArray ?? new JsonArray();
        var count = 0;
        var packages = 0;
        foreach (var entry in results)
        {
            if (entry is not JsonObject result) throw new SecurityPolicyException("Invalid Trivy assessment");
            count += (result["Vulnerabilities"] as JsonArray)?.Count ?? 0;
            packages += (result["Packages"] as JsonArray)?.Count ?? 0;
        }
        return (exitCode != 0 || packages == 0 || count != 0, packages, count);
    }

    internal static void CopyDatabaseMetadata(string cache, string reports)
    {
        var metadata = Path.Combine(cache, "db/metadata.json");
        if (!File.Exists(metadata)) throw new SecurityPolicyException("Trivy did not provide vulnerability database metadata");
        _ = ReadObject(metadata);
        File.Copy(metadata, Path.Combine(reports, "database-metadata.json"), overwrite: true);
    }

    private static async Task<bool> VulnerabilityAuditAsync(string root, string kind, string target, string reports)
    {
        string[] targets;
        if (kind == "image") targets = [target];
        else if (kind == "dependencies")
        {
            var path = Path.GetFullPath(target);
            if (Path.GetFileName(path) != "package-lock.json" || ReadObject(path)["packages"] is not JsonObject { Count: > 0 })
                throw new SecurityPolicyException("Dependency scanning requires a populated npm package-lock.json");
            targets = [path];
        }
        else
        {
            targets = Directory.GetFiles(Path.GetFullPath(target), "*.json").Order(StringComparer.Ordinal).ToArray();
            if (targets.Length == 0) throw new SecurityPolicyException("No CycloneDX JSON SBOMs found");
            foreach (var path in targets)
            {
                var document = ReadObject(path);
                if (document["bomFormat"]?.GetValue<string>() != "CycloneDX" || document["components"] is not JsonArray { Count: > 0 })
                    throw new SecurityPolicyException("Expected a populated CycloneDX SBOM");
            }
        }
        var failed = false;
        File.Delete(Path.Combine(reports, "database-metadata.json"));
        // Empty per-invocation cache prevents stale advisory reuse; SBOMs share a snapshot.
        using var cache = new TemporaryDirectory("trivy-db-");
        for (var index = 0; index < targets.Length; index++)
        {
            var item = targets[index];
            var name = kind is "image" or "dependencies" ? kind : $"sbom-{index}-{Path.GetFileNameWithoutExtension(item)}";
            var report = Path.Combine(reports, name + ".json");
            File.Delete(report);
            var command = new List<string> { kind == "dependencies" ? "fs" : kind, "--config", "", "--ignorefile", "",
                "--cache-dir", cache.Path, "--timeout", "10m", "--scanners", "vuln", "--pkg-types", "os,library",
                "--severity", "HIGH,CRITICAL", "--ignore-unfixed=false", "--exit-code", "23", "--format", "json", "--output", report,
                "--no-progress", "--disable-telemetry", "--skip-version-check", "--list-all-pkgs" };
            if (kind != "dependencies") command.AddRange(["--exit-on-eol", "24"]);
            else
            {
                // Isolate only the lock, never .env, node_modules or unrelated files.
                var inputs = Path.Combine(cache.Path, "inputs");
                Directory.CreateDirectory(inputs);
                var copied = Path.Combine(inputs, "package-lock.json");
                File.Copy(item, copied);
                item = copied;
                command.Add("--include-dev-deps");
            }
            if (kind == "image") command.AddRange(["--image-src", "docker"]);
            command.Add(item);
            var result = await ScanProcessAsync(Tool("trivy"), command, root);
            File.WriteAllText(Path.Combine(reports, name + ".log"), result.StandardOutput + result.StandardError);
            Console.WriteLine(result.StandardOutput);
            Console.WriteLine(result.StandardError);
            if (result.ExitCode is not (0 or 23 or 24)) Console.Error.WriteLine($"Trivy scanner error: exit {result.ExitCode}");
            if (!File.Exists(report))
            {
                failed = true;
                Console.Error.WriteLine($"Trivy did not produce required report: {report}");
                continue;
            }
            var assessment = AssessVulnerabilities(result.ExitCode, ReadObject(report));
            failed |= assessment.Failed;
            if (assessment.Packages == 0) Console.Error.WriteLine("Trivy assessed no packages; refusing an empty vulnerability assessment");
            Console.WriteLine($"Trivy {kind}: {assessment.Packages} package(s), {assessment.Vulnerabilities} HIGH/CRITICAL finding(s); exit {result.ExitCode}; report: {report}");
        }
        CopyDatabaseMetadata(cache.Path, reports);
        return failed;
    }

    private static async Task<int> ScanAsync(string root, string[] args)
    {
        if (args.Length == 0) throw new SecurityPolicyException("Usage: security-scan <secrets|workflows|image|sbom|dependencies|verify-detectors> [target] [--reports path]");
        var mode = args[0];
        if (mode is not ("secrets" or "workflows" or "image" or "sbom" or "dependencies" or "verify-detectors"))
            throw new SecurityPolicyException("Unknown security scan mode");
        string? target = null;
        var reports = Path.Combine(root, "artifacts/security");
        for (var index = 1; index < args.Length; index++)
        {
            if (args[index] == "--reports" && index + 1 < args.Length) reports = args[++index];
            else if (!args[index].StartsWith('-') && target is null) target = args[index];
            else throw new SecurityPolicyException("Invalid security scan arguments");
        }
        if (mode is "image" or "sbom" or "dependencies" && target is null)
            throw new SecurityPolicyException("image requires a built Docker tag; sbom a directory; dependencies an npm lock file");
        reports = Path.GetFullPath(reports);
        Directory.CreateDirectory(reports);
        var failed = mode switch
        {
            "secrets" => (await ScanSecretsAsync(root, root, Path.Combine(reports, "gitleaks.json"))).Failed,
            "workflows" => await WorkflowAuditAsync(root, reports),
            "verify-detectors" => await VerifyDetectorsAsync(root, reports),
            _ => await VulnerabilityAuditAsync(root, mode, target!, reports)
        };
        return failed ? 1 : 0;
    }
}
