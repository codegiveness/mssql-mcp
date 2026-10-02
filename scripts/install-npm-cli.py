#!/usr/bin/env python3
"""Install unmodified npm source with an integrity-locked production dependency tree."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile

ROOT = Path(__file__).resolve().parent.parent
COMMIT = "c276cadf785c1018d82dd287fe7742567055efb9"  # npm v12.2.0
SOURCE_URL = f"https://codeload.github.com/npm/cli/tar.gz/{COMMIT}"
SOURCE_SHA256 = "4fe4c27c222cbade3d70b1154c661c4634ba8fc33ac70f5ffd59a599cd9d038b"
RUNTIME = ROOT / ".config/npm-cli"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache-dir", type=Path, default=Path.home() / ".cache/mssql-mcp-npm")
    parser.add_argument("--print-cli", action="store_true", help="print the previously installed CLI path")
    args = parser.parse_args()
    manifest = json.loads((RUNTIME / "package.json").read_text(encoding="utf-8"))
    destination = args.cache_dir.resolve() / f"npm-{manifest['version']}"
    cli = destination / "npm/bin/npm-cli.js"
    if args.print_cli:
        if not cli.is_file():
            raise RuntimeError("Install the content-locked npm CLI first")
        print(cli)
        return
    if "\n" in str(destination) or "\r" in str(destination):
        raise RuntimeError("The npm cache path must not contain line breaks")
    destination.parent.mkdir(parents=True, exist_ok=True)
    # Native cache storage supports npm's workspace/module symlinks, unlike HGFS.
    with tempfile.TemporaryDirectory(prefix="npm-source-", dir=destination.parent) as temporary:
        staging = Path(temporary)
        archive = staging / "source.tar.gz"
        subprocess.run([
            "curl", "--fail", "--silent", "--show-error", "--location",
            "--proto", "=https", "--proto-redir", "=https",
            "--connect-timeout", "20", "--max-time", "180", "--retry", "2",
            "--output", str(archive), SOURCE_URL,
        ], check=True, timeout=600)
        with archive.open("rb") as downloaded:
            if hashlib.file_digest(downloaded, "sha256").hexdigest() != SOURCE_SHA256:
                raise RuntimeError("SHA-256 verification failed for npm source")
        with tarfile.open(archive) as package:
            package.extractall(staging / "source", filter="data")
        source = staging / "npm"
        (staging / "source" / f"cli-{COMMIT}").rename(source)
        upstream = json.loads((source / "package.json").read_text(encoding="utf-8"))
        for field in ("version", "engines", "dependencies"):
            if upstream[field] != manifest[field]:
                raise RuntimeError(f"npm runtime {field} does not match the pinned upstream source")
        runtime = staging / "runtime"
        runtime.mkdir()
        for name in ("package.json", "package-lock.json"):
            shutil.copyfile(RUNTIME / name, runtime / name)
        # Never execute or retain the source archive's old vendored dependencies.
        shutil.rmtree(source / "node_modules")
        subprocess.run([
            "npm", "ci", "--prefix", str(runtime), "--ignore-scripts", "--omit=dev",
            "--no-audit", "--no-fund", "--bin-links=false", "--engine-strict",
            "--userconfig=/dev/null",
        ], cwd=runtime, check=True, timeout=600)
        installation = staging / "installation"
        packaged_source = installation / "npm"
        packaged_source.mkdir(parents=True)
        # Follow upstream's published file inventory, without executing prepack.
        # Unused workspaces, test fixtures and the original dev lock stay temporary.
        for name in (*upstream["files"], "package.json", "LICENSE", "README.md"):
            item = source / name
            if item.exists():
                target = packaged_source / name
                target.parent.mkdir(parents=True, exist_ok=True)
                item.rename(target)
        (packaged_source / "node_modules").symlink_to("../runtime/node_modules", target_is_directory=True)
        runtime.rename(installation / "runtime")
        if destination.exists():
            shutil.rmtree(destination)
        installation.rename(destination)
    if os.environ.get("GITHUB_ENV"):
        with Path(os.environ["GITHUB_ENV"]).open("a", encoding="utf-8") as github_env:
            github_env.write(f"NPM_CLI={cli}\n")
    print(f"Installed npm {manifest['version']} (source SHA-256 and runtime SRI verified): {cli}")


if __name__ == "__main__":
    main()
