#!/usr/bin/env bash
#
# MCP stdio smoke test for mssql-mcp.
#
# Uses the official MCP Inspector CLI to verify the full JSON-RPC transport:
# initialize -> tools/list -> tools/call.
#
# Usage:
#   export MSSQL_CONNECTION_STRING="Server=...;Database=...;User Id=...;Password=...;Encrypt=True;TrustServerCertificate=True;"
#   ./scripts/mcp-smoke.sh
#
# Expected output:
#   [1] initialize + tools/list: 9 tools found
#   [2] tools/call list_databases: returned N databases
#   ALL CHECKS PASSED
#
# Exit codes:
#   0 - all checks passed
#   1 - one or more checks failed

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/.."

if [[ -z "${MSSQL_CONNECTION_STRING:-}" ]]; then
  if [[ -f .env ]]; then
    set -a
    # shellcheck disable=SC1091
    source .env
    set +a
  fi
fi

if [[ -z "${MSSQL_CONNECTION_STRING:-}" ]]; then
  echo "mcp-smoke: MSSQL_CONNECTION_STRING not set and .env not found." >&2
  echo "  Set it via: export MSSQL_CONNECTION_STRING=\"Server=...;...\"" >&2
  exit 1
fi

BINARY="src/mssql-mcp/bin/Debug/net10.0/mssql-mcp"
if [[ ! -x "$BINARY" ]]; then
  echo "mcp-smoke: binary not found, building..." >&2
  dotnet build src/mssql-mcp --nologo -v q 2>&1 || {
    echo "mcp-smoke: build failed" >&2
    exit 1
  }
fi

INSPECTOR="npx --yes @modelcontextprotocol/inspector --cli"
PASS=0
FAIL=0

ok() { echo "[PASS] $*"; PASS=$((PASS + 1)); }
bad() { echo "[FAIL] $*" >&2; FAIL=$((FAIL + 1)); }

# [1] initialize + tools/list
echo "=== [1] initialize + tools/list ==="
TOOLS_JSON=$($INSPECTOR "$BINARY" --method tools/list 2>/dev/null) || {
  bad "tools/list: inspector exited $?"
  exit 1
}
TOOL_COUNT=$(echo "$TOOLS_JSON" | python3 -c "import sys,json; print(len(json.load(sys.stdin)['tools']))" 2>/dev/null) || {
  bad "tools/list: failed to parse response"
  exit 1
}

if [[ "$TOOL_COUNT" -eq 9 ]]; then
  ok "tools/list: $TOOL_COUNT tools found"
else
  bad "tools/list: expected 9 tools, got $TOOL_COUNT"
fi

# [2] tools/call list_databases
echo "=== [2] tools/call list_databases ==="
DB_JSON=$($INSPECTOR "$BINARY" --method tools/call --tool-name list_databases 2>/dev/null) || {
  bad "list_databases: inspector exited $?"
  exit 1
}
DB_COUNT=$(echo "$DB_JSON" | python3 -c "
import sys, json
resp = json.load(sys.stdin)
text = next((c['text'] for c in resp.get('content', []) if c.get('type') == 'text'), '[]')
dbs = json.loads(text)
print(len(dbs))
" 2>/dev/null) || {
  bad "list_databases: failed to parse response"
  exit 1
}

if [[ "$DB_COUNT" -gt 0 ]]; then
  ok "list_databases: returned $DB_COUNT databases"
else
  bad "list_databases: returned 0 databases"
fi

# Summary
echo ""
echo "================================"
echo "  PASSED: $PASS  FAILED: $FAIL"
echo "================================"

if [[ "$FAIL" -gt 0 ]]; then
  exit 1
fi
echo "ALL CHECKS PASSED"
exit 0
