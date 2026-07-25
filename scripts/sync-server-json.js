'use strict';

// Syncs the version from .release-please-manifest.json into all three
// version fields of the MCP server manifest (server.json):
//   - top-level `version`
//   - packages[0].version (registryType: npm)
//   - packages[1].version (registryType: nuget)
//
// Idempotent: re-running on an already-synced file produces byte-identical
// output and reports changed=false. Touches only the three version fields.
//
// ADR-0034: release-please owns the manifest version; this script stamps
// server.json's non-standard three-field layout.
//
// Run directly:  node scripts/sync-server-json.js
//   (reads ../.release-please-manifest.json, writes ../server.json)
// Programmatic: const { syncServerJson } = require('./scripts/sync-server-json.js');

const fs = require('fs');
const path = require('path');

const REPO_ROOT = path.join(__dirname, '..');

function readJson(filePath, label) {
  let raw;
  try {
    raw = fs.readFileSync(filePath, 'utf8');
  } catch (e) {
    throw new Error('cannot read ' + label + ' at ' + filePath + ': ' + e.message);
  }
  try {
    return JSON.parse(raw);
  } catch (e) {
    throw new Error('malformed ' + label + ' at ' + filePath + ': ' + e.message);
  }
}

// Returns { changed, output }.
//   changed: true if any version field would change, false if already synced.
//   output:  the serialized server.json (2-space indent, trailing newline).
function syncServerJson({ manifestPath, serverJsonPath }) {
  const manifest = readJson(manifestPath, 'manifest');
  const manifestVersion = manifest.version;
  if (typeof manifestVersion !== 'string' || manifestVersion.length === 0) {
    throw new Error('manifest at ' + manifestPath + ' has no "version" string');
  }

  const server = readJson(serverJsonPath, 'server.json');

  let changed = false;
  if (server.version !== manifestVersion) {
    server.version = manifestVersion;
    changed = true;
  }
  if (!Array.isArray(server.packages) || server.packages.length < 2) {
    throw new Error('server.json at ' + serverJsonPath + ' must have packages[0] (npm) and packages[1] (nuget)');
  }
  if (server.packages[0].version !== manifestVersion) {
    server.packages[0].version = manifestVersion;
    changed = true;
  }
  if (server.packages[1].version !== manifestVersion) {
    server.packages[1].version = manifestVersion;
    changed = true;
  }

  const output = JSON.stringify(server, null, 2) + '\n';
  return { changed, output };
}

if (require.main === module) {
  const manifestPath = path.join(REPO_ROOT, '.release-please-manifest.json');
  const serverJsonPath = path.join(REPO_ROOT, 'server.json');
  const { changed, output } = syncServerJson({ manifestPath, serverJsonPath });
  fs.writeFileSync(serverJsonPath, output);
  if (changed) {
    console.log('server.json synced to version ' + JSON.parse(output).version);
  } else {
    console.log('server.json already in sync');
  }
}

module.exports = { syncServerJson };
