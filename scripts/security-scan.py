#!/usr/bin/env python3
"""Run pinned scanners without loading .env; exit nonzero for findings or errors."""

import argparse
import json
import os
from pathlib import Path
import secrets
import string
import subprocess
import sys
import tempfile

from importlib.util import module_from_spec, spec_from_file_location

ROOT = Path(__file__).resolve().parent.parent
spec = spec_from_file_location("security_installer", ROOT / "scripts/install-security-tools.py")
installer = module_from_spec(spec)
spec.loader.exec_module(installer)


def tool(name):
    executable = installer.tools_directory() / name
    if not executable.is_file():
        raise RuntimeError(f"Install the pinned tool: python3 scripts/install-security-tools.py {name}")
    return str(executable)


def scanner_environment():
    # Local scanner configuration must not silently weaken the repository policy.
    return {key: value for key, value in os.environ.items()
            if not key.startswith(("GITLEAKS_", "TRIVY_", "ZIZMOR_"))}


def run(command, *, cwd=ROOT, timeout=660, capture=False, input_text=None):
    return subprocess.run(command, cwd=cwd, env=scanner_environment(), timeout=timeout,
                          capture_output=capture, text=True, input=input_text, check=False)


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def scan_secrets(repository, report):
    # Git mode reads committed patches only, never the ignored/local .env worktree.
    shallow = run(["git", "rev-parse", "--is-shallow-repository"], cwd=repository, capture=True)
    if shallow.returncode != 0 or shallow.stdout.strip() != "false":
        raise RuntimeError("Full-history scanning requires a non-shallow Git repository (fetch-depth: 0)")
    with tempfile.TemporaryDirectory(prefix="gitleaks-report-") as temporary:
        raw_report = Path(temporary) / "findings.json"
        result = run([
            tool("gitleaks"), "git", str(repository), "--config", str(ROOT / ".gitleaks.toml"),
            "--log-opts=--all --full-history --diff-merges=separate", "--redact=100",
            "--ignore-gitleaks-allow", "--gitleaks-ignore-path", temporary,
            "--exit-code", "23", "--timeout", "600", "--no-banner", "--no-color",
            "--report-format", "json", "--report-path", str(raw_report),
        ], capture=True)
        # Never print raw scanner output: commit messages and multi-secret lines can
        # contain credentials outside the matched/redacted group. Persist metadata only.
        findings = json.loads(raw_report.read_text()) if raw_report.is_file() else []
        safe_keys = ("RuleID", "File", "StartLine", "EndLine", "StartColumn", "EndColumn", "Commit", "Fingerprint")
        safe = [{**{key: finding[key] for key in safe_keys if key in finding}, "Secret": "REDACTED"}
                for finding in findings]
        write_json(report, safe)
        if result.returncode not in (0, 23):
            raise RuntimeError(f"Gitleaks failed (exit {result.returncode}); raw output withheld to protect credentials")
        if not raw_report.is_file():
            raise RuntimeError("Gitleaks did not produce its required report")
        if any(finding.get("Secret") != "REDACTED" for finding in findings):
            raise RuntimeError("Gitleaks did not redact every finding")
        print(f"Gitleaks: {len(safe)} redacted finding(s); metadata report: {report}")
        for finding in safe:
            print(f"  {finding.get('RuleID')}: {finding.get('File')}:{finding.get('StartLine')} commit {finding.get('Commit')}")
        return result.returncode != 0, findings


def workflow_audit(reports):
    workflows = sorted((ROOT / ".github/workflows").glob("*.yml"))
    workflows += sorted((ROOT / ".github/workflows").glob("*.yaml"))
    if not workflows:
        raise RuntimeError("No workflows found")
    shellcheck = installer.tools_directory() / "shellcheck"
    actionlint = run([
        tool("actionlint"), "-shellcheck", str(shellcheck) if shellcheck.is_file() else "shellcheck",
        "-format", "{{json .}}", *map(str, workflows),
    ], capture=True)
    (reports / "actionlint.json").write_text(actionlint.stdout, encoding="utf-8")
    (reports / "actionlint.log").write_text(actionlint.stderr, encoding="utf-8")
    # JSON (not SARIF) preserves zizmor's failure exit codes for security findings.
    # Offline analysis is tokenless and deterministic on untrusted fork PRs.
    zizmor = run([
        tool("zizmor"), "--offline", "--strict-collection", "--persona=regular",
        "--format=json", "--color=never", *map(str, workflows),
    ], capture=True)
    (reports / "zizmor.json").write_text(zizmor.stdout, encoding="utf-8")
    (reports / "zizmor.log").write_text(zizmor.stderr, encoding="utf-8")
    print(actionlint.stdout, actionlint.stderr, zizmor.stdout, zizmor.stderr, sep="\n")
    print(f"actionlint exit: {actionlint.returncode}; zizmor exit: {zizmor.returncode}")
    return actionlint.returncode != 0 or zizmor.returncode != 0


def vulnerability_audit(kind, target, reports):
    if kind == "image":
        targets = [target]
    elif kind == "dependencies":
        lock = Path(target).resolve()
        if lock.name != "package-lock.json" or not json.loads(lock.read_text(encoding="utf-8")).get("packages"):
            raise RuntimeError("Dependency scanning requires a populated npm package-lock.json")
        targets = [lock]
    else:
        directory = Path(target).resolve()
        targets = sorted(directory.glob("*.json"))
        if not targets:
            raise RuntimeError(f"No CycloneDX JSON SBOMs found in {directory}")
        for path in targets:
            document = json.loads(path.read_text(encoding="utf-8"))
            if document.get("bomFormat") != "CycloneDX" or not document.get("components"):
                raise RuntimeError(f"Expected a populated CycloneDX SBOM: {path}")
    failed = False
    # A failed rerun must never present an earlier invocation's findings as fresh.
    (reports / "database-metadata.json").unlink(missing_ok=True)
    # An empty per-invocation cache forces fresh advisory data, never a stale CI cache.
    # Within one SBOM invocation, every file uses the same downloaded DB snapshot.
    with tempfile.TemporaryDirectory(prefix="trivy-db-") as cache:
        for index, item in enumerate(targets):
            name = kind if kind in ("image", "dependencies") else f"sbom-{index}-{Path(item).stem}"
            report = reports / f"{name}.json"
            report.unlink(missing_ok=True)
            command = [
                tool("trivy"), "fs" if kind == "dependencies" else kind, "--config", "", "--ignorefile", "",
                "--cache-dir", cache, "--timeout", "10m", "--scanners", "vuln",
                "--pkg-types", "os,library", "--severity", "HIGH,CRITICAL",
                "--ignore-unfixed=false", "--exit-code", "23",
                "--format", "json", "--output", str(report), "--no-progress",
                "--disable-telemetry", "--skip-version-check", "--list-all-pkgs",
            ]
            if kind != "dependencies":
                command += ["--exit-on-eol", "24"]
            else:
                # Scan only the committed lock input, never the surrounding .env,
                # node_modules, or unrelated workstation/container files.
                inputs = Path(cache) / "inputs"
                inputs.mkdir()
                copied_lock = inputs / "package-lock.json"
                copied_lock.write_bytes(Path(item).read_bytes())
                item = copied_lock
                command += ["--include-dev-deps"]
            if kind == "image":
                # Assess the built Docker image, never an unrelated registry tag.
                command += ["--image-src", "docker"]
            command.append(str(item))
            result = run(command, capture=True)
            (reports / f"{name}.log").write_text(result.stdout + result.stderr, encoding="utf-8")
            print(result.stdout, result.stderr, sep="\n")
            if result.returncode not in (0, 23, 24):
                failed = True
                print(f"Trivy scanner error: exit {result.returncode}", file=sys.stderr)
            elif result.returncode != 0:
                failed = True
            if not report.is_file():
                failed = True
                print(f"Trivy did not produce required report: {report}", file=sys.stderr)
            else:
                document = json.loads(report.read_text(encoding="utf-8"))
                results = document.get("Results") or []
                count = sum(len(result.get("Vulnerabilities") or []) for result in results)
                packages = sum(len(result.get("Packages") or []) for result in results)
                if packages == 0:
                    failed = True
                    print("Trivy assessed no packages; refusing an empty vulnerability assessment", file=sys.stderr)
                print(f"Trivy {kind}: {packages} package(s), {count} HIGH/CRITICAL finding(s); exit {result.returncode}; report: {report}")
        metadata = Path(cache) / "db/metadata.json"
        if not metadata.is_file():
            raise RuntimeError("Trivy did not provide vulnerability database metadata")
        (reports / "database-metadata.json").write_bytes(metadata.read_bytes())
    return failed


def verify_detectors(reports):
    # All credentials are synthetic, generated only in a disposable repository.
    alphabet = string.ascii_letters + string.digits
    password = "Synthetic" + "".join(secrets.choice(alphabet) for _ in range(24)) + "!"
    token = "ghp_" + "".join(secrets.choice(alphabet) for _ in range(36))
    password_key = "Pass" + "word"
    pwd_key = "P" + "WD"
    container_key = "MSSQL_SA_" + "PASSWORD"
    fixture_value = "sec" + "ret"
    container_value = "YourStrong!" + "Passw0rd"
    samples = {
        "plain.sql": f"Server=fixture.invalid;{password_key}={password};Database=example;",
        "pwd.sql": f"{pwd_key}={password};Server=fixture.invalid;",
        "quoted.sql": f'Server=fixture.invalid;{password_key}="{password};tail";Database=example;',
        "single.sql": f"Server=fixture.invalid;{password_key}='{password};tail';Database=example;",
        "escaped.cs": f'Server=fixture.invalid;{password_key}=\\"{password};tail\\";Database=example;',
        "braced.sql": f"Server=fixture.invalid;{password_key}={{{password};tail}};Database=example;",
        "container.yml": f"{container_key}: {password}",
        "provider.txt": f"github_token = '{token}'",
        # A known test value outside its named fixture must still be detected.
        "tests/new-consumer.cs": f"Server=fixture.invalid;{password_key}={fixture_value};",
        "tests/mssql-mcp.Core.Tests/FileLoggerProviderTests.cs": f"Server=fixture.invalid;{password_key}={password};",
        ".github/workflows/ci.yml": f"{container_key}: {password}",
    }
    with tempfile.TemporaryDirectory(prefix="security-detectors-") as temporary:
        repository = Path(temporary)
        for command in (["git", "init", "--quiet"], ["git", "config", "user.email", "fixture@example.invalid"],
                        ["git", "config", "user.name", "Synthetic detector verification"]):
            subprocess.run(command, cwd=repository, check=True, capture_output=True)
        for filename, content in samples.items():
            path = repository / filename
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content + "\n", encoding="utf-8")
        subprocess.run(["git", "add", "."], cwd=repository, check=True, capture_output=True)
        subprocess.run(["git", "commit", "--quiet", "-m", "Synthetic positives"], cwd=repository, check=True, capture_output=True)
        subprocess.run(["git", "rm", "-r", "."], cwd=repository, check=True, capture_output=True)
        subprocess.run(["git", "commit", "--quiet", "-m", "Delete positives to verify history scanning"], cwd=repository, check=True, capture_output=True)
        failed, findings = scan_secrets(repository, reports / "detector-positive.json")
        detected = {finding["File"] for finding in findings}
        if not failed or not set(samples).issubset(detected):
            raise RuntimeError(f"Detector missed positive fixture(s): {sorted(set(samples) - detected)}")
        serialized = json.dumps(findings)
        if password in serialized or token in serialized:
            raise RuntimeError("Synthetic secret was not fully redacted")
    with tempfile.TemporaryDirectory(prefix="security-negative-") as temporary:
        repository = Path(temporary)
        subprocess.run(["git", "init", "--quiet"], cwd=repository, check=True, capture_output=True)
        (repository / "safe.sql").write_text(
            "Server=fixture.invalid;Integrated Security=true;\n"
            "Server=fixture.invalid;Password=;\n"
            "Server=<your-server>;Password=<your-password>;\n", encoding="utf-8")
        allowed = repository / "tests/mssql-mcp.Core.Tests/FileLoggerProviderTests.cs"
        allowed.parent.mkdir(parents=True)
        allowed.write_text(f"Server=fixture.invalid;{password_key}={fixture_value};\n", encoding="utf-8")
        container = repository / ".github/workflows/ci.yml"
        container.parent.mkdir(parents=True)
        container.write_text(f"{container_key}: {container_value}\n", encoding="utf-8")
        (repository / ".gitignore").write_text(".env\n", encoding="utf-8")
        (repository / ".env").write_text(f"Server=fixture.invalid;{password_key}={password};", encoding="utf-8")
        subprocess.run(["git", "add", "."], cwd=repository, check=True, capture_output=True)
        subprocess.run(["git", "-c", "user.name=Synthetic detector verification", "-c", "user.email=fixture@example.invalid",
                        "commit", "--quiet", "-m", "Synthetic negatives"], cwd=repository, check=True, capture_output=True)
        failed, findings = scan_secrets(repository, reports / "detector-negative.json")
        if failed or findings:
            raise RuntimeError("Negative fixtures or ignored .env were incorrectly detected")
    print("Detector verification passed: provider/SQL positives, deleted history, redaction, scoped exceptions, negatives and ignored .env")
    return False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("secrets", "workflows", "image", "sbom", "dependencies", "verify-detectors"))
    parser.add_argument("target", nargs="?")
    parser.add_argument("--reports", type=Path, default=ROOT / "artifacts/security")
    args = parser.parse_args()
    if args.mode in ("image", "sbom", "dependencies") and not args.target:
        parser.error("image requires a built Docker tag; sbom a directory; dependencies an npm lock file")
    reports = args.reports.resolve()
    reports.mkdir(parents=True, exist_ok=True)
    try:
        if args.mode == "secrets":
            failed, _ = scan_secrets(ROOT, reports / "gitleaks.json")
        elif args.mode == "workflows":
            failed = workflow_audit(reports)
        elif args.mode == "verify-detectors":
            failed = verify_detectors(reports)
        else:
            failed = vulnerability_audit(args.mode, args.target, reports)
        return 1 if failed else 0
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        # Do not echo raw scanner exception messages or input content.
        message = str(error) if isinstance(error, RuntimeError) else type(error).__name__
        print(f"Security scan failed: {message}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
