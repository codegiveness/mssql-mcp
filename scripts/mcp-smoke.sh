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

INSPECTOR_CONFIG="$(mktemp)"
trap 'rm -f "$INSPECTOR_CONFIG"' EXIT
node - "$INSPECTOR_CONFIG" "$BINARY" <<'NODE'
const fs = require('node:fs');
const path = require('node:path');
const config = {
  mcpServers: {
    'mssql-mcp': {
      command: path.resolve(process.argv[3]),
      env: { MSSQL_CONNECTION_STRING: process.env.MSSQL_CONNECTION_STRING },
    },
  },
};
fs.writeFileSync(process.argv[2], JSON.stringify(config));
NODE
INSPECTOR_CLI=".config/npm-tools/node_modules/@modelcontextprotocol/inspector/clients/launcher/build/index.js"
if [[ ! -f "$INSPECTOR_CLI" ]]; then
  npm ci --prefix .config/npm-tools --ignore-scripts --no-audit --no-fund \
    --bin-links=false --engine-strict --userconfig=/dev/null
fi
INSPECTOR=(env MCP_INSPECTOR_SECRET_STORE=memory node "$INSPECTOR_CLI" --cli \
  --config "$INSPECTOR_CONFIG" --server mssql-mcp --format json \
  --stored-auth-only --connect-timeout 15000)
PASS=0
FAIL=0

ok() { echo "[PASS] $*"; PASS=$((PASS + 1)); }
bad() { echo "[FAIL] $*" >&2; FAIL=$((FAIL + 1)); }

# [1] initialize + tools/list
echo "=== [1] initialize + tools/list ==="
TOOLS_JSON=$("${INSPECTOR[@]}" --method tools/list 2>/dev/null) || {
  bad "tools/list: inspector exited $?"
  exit 1
}
TOOL_COUNT=$(echo "$TOOLS_JSON" | node -e "const r = JSON.parse(require('node:fs').readFileSync(0, 'utf8')); console.log(r.result.tools.length);" 2>/dev/null) || {
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
DB_JSON=$("${INSPECTOR[@]}" --method tools/call --tool-name list_databases 2>/dev/null) || {
  bad "list_databases: inspector exited $?"
  exit 1
}
DB_COUNT=$(echo "$DB_JSON" | node -e "
const response = JSON.parse(require('node:fs').readFileSync(0, 'utf8')).result;
if (response.isError) throw new Error('list_databases returned an error');
const text = response.content.find(c => c.type === 'text')?.text;
const databases = JSON.parse(text);
if (!Array.isArray(databases)) throw new Error('Expected database array');
console.log(databases.length);
" 2>/dev/null) || {
  bad "list_databases: failed to parse response"
  exit 1
}

if [[ "$DB_COUNT" -gt 0 ]]; then
  ok "list_databases: returned $DB_COUNT databases"
else
  bad "list_databases: returned 0 databases"
fi

# [3] idempotentHint annotations
echo "=== [3] idempotentHint annotations ==="
IDEMPOTENT_OK=true
IDEMPOTENT_RESULT=$(echo "$TOOLS_JSON" | node -e "
const expectedTrue = new Set([
  'list_databases', 'list_schemas', 'list_objects', 'get_object_details',
  'explain_query', 'analyze_indexes', 'get_top_queries', 'analyze_db_health',
]);
const tools = JSON.parse(require('node:fs').readFileSync(0, 'utf8')).result.tools;
const mismatches = [];
for (const tool of tools) {
  const name = tool.name;
  if (!expectedTrue.has(name) && name !== 'execute_sql') continue;
  const want = expectedTrue.has(name);
  const hint = tool.annotations?.idempotentHint;
  if (hint !== want) mismatches.push(name + ': expected ' + want + ', got ' + hint);
}
if (mismatches.length) {
  console.log(mismatches.join('\n'));
  process.exit(1);
}
console.log('8 read-only=true, execute_sql=false');
" 2>/dev/null) || IDEMPOTENT_OK=false

if $IDEMPOTENT_OK; then
  ok "idempotentHint: $IDEMPOTENT_RESULT"
else
  while IFS= read -r line; do
    bad "idempotentHint: $line"
  done <<< "$IDEMPOTENT_RESULT"
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
