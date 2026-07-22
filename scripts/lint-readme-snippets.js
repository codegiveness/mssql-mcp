'use strict';

// Lints fenced JSON code blocks in README.md:
//   1. Each block parses as valid JSON (strips // line comments and trailing commas).
//   2. Each block has an "mcpServers" key.
//   3. Each mcpServers entry has "command" and at least one of "args" or "env".
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

  if (!json.mcpServers || typeof json.mcpServers !== 'object') {
    fail(i, 'missing "mcpServers" key');
    return;
  }

  for (const [name, cfg] of Object.entries(json.mcpServers)) {
    if (!cfg.command) {
      fail(i, 'server "' + name + '" missing "command"');
    }
    if (!cfg.args && !cfg.env) {
      fail(i, 'server "' + name + '" needs "args" or "env"');
    }
  }
});

if (failures > 0) {
  console.error('\n' + failures + ' snippet lint(s) failed.');
  process.exit(1);
} else {
  console.log('All ' + blocks.length + ' README JSON snippet(s) valid.');
}
