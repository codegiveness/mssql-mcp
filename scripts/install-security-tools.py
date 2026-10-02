#!/usr/bin/env python3
"""Install reviewed Linux x86-64 scanner releases after SHA-256 verification."""

import argparse
import hashlib
import os
from pathlib import Path
import platform
import subprocess
import tarfile
import tempfile

# SHA-256 digests are from the upstream GitHub release asset metadata.
# Keep URLs and content pins together; updates require reviewing both.
TOOLS = {
    "gitleaks": (
        "8.30.1",
        "https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/gitleaks_8.30.1_linux_x64.tar.gz",
        "551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb",
        "gitleaks",
    ),
    "zizmor": (
        "1.30.1",
        "https://github.com/zizmorcore/zizmor/releases/download/v1.30.1/zizmor-x86_64-unknown-linux-gnu.tar.gz",
        "e65324f4430c2717591937edcec90ccbefaf14c174f8ec9415e03ca875b46e1a",
        "zizmor",
    ),
    "actionlint": (
        "1.7.12",
        "https://github.com/rhysd/actionlint/releases/download/v1.7.12/actionlint_1.7.12_linux_amd64.tar.gz",
        "8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8",
        "actionlint",
    ),
    "shellcheck": (
        "0.11.0",
        "https://github.com/koalaman/shellcheck/releases/download/v0.11.0/shellcheck-v0.11.0.linux.x86_64.tar.xz",
        "8c3be12b05d5c177a04c29e3c78ce89ac86f1595681cab149b65b97c4e227198",
        "shellcheck-v0.11.0/shellcheck",
    ),
    "trivy": (
        "0.75.0",
        "https://github.com/aquasecurity/trivy/releases/download/v0.75.0/trivy_0.75.0_Linux-64bit.tar.gz",
        "c6e65abddb348e25f10549df887045629cf28cc72453cd1c63acb717316b3f3f",
        "trivy",
    ),
}


def tools_directory():
    return Path(os.environ.get("SECURITY_TOOLS_DIR", Path.home() / ".cache/mssql-mcp-security/bin")).resolve()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tools", nargs="+", choices=TOOLS)
    args = parser.parse_args()
    if platform.system() != "Linux" or platform.machine() != "x86_64":
        parser.error("the pinned binaries support Linux x86-64 only")
    destination = tools_directory()
    destination.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="security-tools-") as temporary:
        archive = Path(temporary) / "release.tar"
        for name in args.tools:
            version, url, digest, member_name = TOOLS[name]
            subprocess.run([
                "curl", "--fail", "--silent", "--show-error", "--location",
                "--proto", "=https", "--proto-redir", "=https",
                "--connect-timeout", "20", "--max-time", "180",
                "--retry", "2", "--output", str(archive), url,
            ], check=True, timeout=600)
            with archive.open("rb") as downloaded:
                actual = hashlib.file_digest(downloaded, "sha256").hexdigest()
            if actual != digest:
                raise RuntimeError(f"SHA-256 verification failed for {name} {version}")
            # Extract exactly the reviewed regular file, never arbitrary archive paths.
            with tarfile.open(archive) as package:
                member = package.getmember(member_name)
                if not member.isfile():
                    raise RuntimeError(f"invalid executable member for {name}")
                with package.extractfile(member) as source:
                    target = destination / name
                    target.write_bytes(source.read())
                    target.chmod(0o755)
            print(f"Installed {name} {version} (SHA-256 verified) in {destination}")


if __name__ == "__main__":
    main()
