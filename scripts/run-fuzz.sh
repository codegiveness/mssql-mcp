#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
output=${1:-$(mktemp -d /tmp/mssql-mcp-fuzz.XXXXXX)}
seconds=${2:-60}
if [[ ! "$seconds" =~ ^[0-9]+$ ]] || (( seconds < 1 || seconds > 120 )); then
  echo 'Campaign duration must be between 1 and 120 seconds.' >&2
  exit 1
fi
mkdir -p "$output"
output=$(realpath "$output")
if [[ -e "$output/target" || -e "$output/tools" || -e "$output/corpus" || -e "$output/probe" ]]; then
  echo 'Use a fresh output directory; previous corpora and findings are never overwritten.' >&2
  exit 1
fi
mkdir -p "$output/corpus" "$output/probe" "$output/findings"
cp "$root"/fuzz/corpus/*.sql "$output/corpus/"

project="$root/fuzz/mssql-mcp.Fuzz/mssql-mcp.Fuzz.csproj"
dotnet restore "$project" --locked-mode
dotnet publish "$project" -c Release --no-restore -p:UseAppHost=false -o "$output/target"
dotnet tool install SharpFuzz.CommandLine --version 2.3.0 --tool-path "$output/tools"

# Both application policy and ScriptDom must contribute managed branch coverage.
"$output/tools/sharpfuzz" "$output/target/mssql-mcp.Core.dll" mssql_mcp.Core.Guard \
  | tee "$output/instrumentation.log"
"$output/tools/sharpfuzz" "$output/target/Microsoft.SqlServer.TransactSql.ScriptDom.dll" \
  | tee -a "$output/instrumentation.log"

# Upstream-supported native bridge, pinned by commit and verified source hash.
curl --fail --silent --show-error \
  https://raw.githubusercontent.com/Metalnem/libfuzzer-dotnet/bd39d4e88d715ab460a929943645be2a186cde52/libfuzzer-dotnet.cc \
  --output "$output/libfuzzer-dotnet.cc"
printf '%s  %s\n' 90f019e2e9ad3a0b93c7ecc2c5afb2fbfc8b5aab6aac51c7e0d349ec79354f36 "$output/libfuzzer-dotnet.cc" \
  | sha256sum --check
clang++ -fsanitize=fuzzer "$output/libfuzzer-dotnet.cc" -o "$output/libfuzzer-dotnet"

# A separate intentional managed exception must be reported as a crash artifact.
# It is validation of the engine, never counted as a discovered product defect.
set +e
MSSQL_FUZZ_CRASH_PROBE=1 timeout --kill-after=5s 20s "$output/libfuzzer-dotnet" \
  --target_path=dotnet --target_arg="$output/target/mssql-mcp.Fuzz.dll" \
  -runs=1 -max_len=4096 -artifact_prefix="$output/probe/" \
  2>&1 | tee "$output/crash-probe.log"
probe_status=${PIPESTATUS[0]}
set -e
if (( probe_status == 0 )) || ! compgen -G "$output/probe/crash-*" >/dev/null \
  || ! grep -q 'Intentional fuzz-engine crash-detection probe' "$output/crash-probe.log"; then
  echo 'The intentional crash probe did not produce the expected crash evidence.' >&2
  exit 1
fi

# Fixed mutation seed makes short CI campaigns reproducible; new corpus entries
# and real crash/timeout artifacts remain in the uploaded output directory.
env -u MSSQL_FUZZ_CRASH_PROBE timeout --kill-after=5s "$((seconds + 20))s" \
  "$output/libfuzzer-dotnet" --target_path=dotnet --target_arg="$output/target/mssql-mcp.Fuzz.dll" \
  -seed=1 -max_len=4096 -timeout=5 -max_total_time="$seconds" \
  -dict="$root/fuzz/sql.dict" -artifact_prefix="$output/findings/" \
  "$output/corpus" 2>&1 | tee "$output/campaign.log"
