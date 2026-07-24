'use strict';

// Lints fenced JSON code blocks in README.md:
//   1. Each block parses as valid JSON (strips // line comments and trailing commas).
//   2. Blocks containing an "mcpServers" key (standard MCP clients) OR an "mcp" key
//      (opencode's native config shape) are validated as MCP server configs.
//   3. Each validated server entry has "command" and at least one of "args" or "env".
//   4. JSON blocks without "mcpServers"/"mcp" keys are documentation examples
//      (tool I/O, return shapes) and are validated for JSON syntax only.
//
// Also validates that README badge image URLs are well-formed HTTP(S) URLs
// (syntax check only, no network fetch).
//
// Run: node scripts/lint-readme-snippets.js
// Exits non-zero on any failure.

const fs = require('fs');
const path = require('path');

const readme = fs.readFileSync(path.join(__dirname, '..', 'README.md'), 'utf8');

const blocks = [];
const fenceRegex = /```jsonc?\n([\s\S]*?)```/g;
let match;
while ((match = fenceRegex.exec(readme)) !== null) {
  blocks.push(match[1]);
}

let failures = 0;

function fail(blockIndex, msg) {
  failures++;
  console.error('FAIL - README JSON block #' + (blockIndex + 1) + ': ' + msg);
}

blocks.forEach((raw, i) => {
  const stripped = raw
    .split('\n')
    .map((line) => line.replace(/\/\/.*$/, ''))
    .join('\n')
    .replace(/,(\s*[}\]])/g, '$1');

  let json;
  try {
    json = JSON.parse(stripped);
  } catch (e) {
    fail(i, 'invalid JSON: ' + e.message);
    return;
  }

  // Standard MCP client shape.
  if (json.mcpServers && typeof json.mcpServers === 'object') {
    for (const [name, cfg] of Object.entries(json.mcpServers)) {
      if (!cfg.command) {
        fail(i, 'server "' + name + '" missing "command"');
      }
      if (!cfg.args && !cfg.env) {
        fail(i, 'server "' + name + '" needs "args" or "env"');
      }
    }
    return;
  }

  // opencode native shape: { "mcp": { "mssql-mcp": { "type": "local", "command": [...], "enabled": true } } }
  if (json.mcp && typeof json.mcp === 'object') {
    for (const [name, cfg] of Object.entries(json.mcp)) {
      if (!cfg.command) {
        fail(i, 'server "' + name + '" missing "command"');
      }
      if (!cfg.args && !cfg.env) {
        fail(i, 'server "' + name + '" needs "args" or "env"');
      }
    }
    return;
  }

  // JSON blocks without mcpServers/mcp keys are documentation examples (tool I/O,
  // return shapes). Validated for JSON syntax only — no config structure to check.
  return;
});

function validateBadgeImageUrls() {
  const badgeRegex = /!\[[^\]]*\]\(([^)]+)\)/g;
  let match;
  while ((match = badgeRegex.exec(readme)) !== null) {
    const url = match[1];
    if (!url.startsWith('http://') && !url.startsWith('https://')) {
      failures++;
      console.error('FAIL - README badge image URL is malformed: ' + url);
    }
  }
}

validateBadgeImageUrls();

if (failures > 0) {
  console.error('\n' + failures + ' lint(s) failed.');
  process.exit(1);
} else {
  console.log('All ' + blocks.length + ' README JSON snippet(s) valid.');
  console.log('All README badge image URLs are well-formed.');
}
