'use strict';

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

function syncCsproj(csprojPath, version) {
  let content = fs.readFileSync(csprojPath, 'utf8');
  const regex = /<VersionPrefix>\s*([^<\s]+)\s*<\/VersionPrefix>/;
  const match = content.match(regex);
  if (!match) {
    throw new Error('no <VersionPrefix> found in ' + csprojPath);
  }
  if (match[1] === version) {
    return { changed: false, content };
  }
  content = content.replace(regex, '<VersionPrefix>' + version + '</VersionPrefix>');
  return { changed: true, content };
}

function syncPackageJson(packageJsonPath, version) {
  const pkg = readJson(packageJsonPath, 'package.json');
  let changed = false;
  if (pkg.version !== version) {
    pkg.version = version;
    changed = true;
  }
  if (pkg.optionalDependencies && typeof pkg.optionalDependencies === 'object') {
    for (const dep of Object.keys(pkg.optionalDependencies)) {
      if (pkg.optionalDependencies[dep] !== version) {
        pkg.optionalDependencies[dep] = version;
        changed = true;
      }
    }
  }
  return { changed, output: JSON.stringify(pkg, null, 2) + '\n' };
}

function syncServerJson(serverJsonPath, version) {
  const server = readJson(serverJsonPath, 'server.json');
  let changed = false;
  if (server.version !== version) {
    server.version = version;
    changed = true;
  }
  if (!Array.isArray(server.packages) || server.packages.length < 2) {
    throw new Error('server.json must have packages[0] (npm) and packages[1] (nuget)');
  }
  if (server.packages[0].version !== version) {
    server.packages[0].version = version;
    changed = true;
  }
  if (server.packages[1].version !== version) {
    server.packages[1].version = version;
    changed = true;
  }
  return { changed, output: JSON.stringify(server, null, 2) + '\n' };
}

function syncAllStamps({ manifestPath, csprojPath, packageJsonPath, serverJsonPath }) {
  const manifest = readJson(manifestPath, 'manifest');
  const version = manifest['.'];
  if (typeof version !== 'string' || version.length === 0) {
    throw new Error('manifest at ' + manifestPath + ' has no "." version string');
  }

  const results = [];
  let anyChanged = false;

  const csproj = syncCsproj(csprojPath, version);
  if (csproj.changed) {
    fs.writeFileSync(csprojPath, csproj.content);
    anyChanged = true;
  }
  results.push({ file: 'csproj', changed: csproj.changed });

  const pkg = syncPackageJson(packageJsonPath, version);
  if (pkg.changed) {
    fs.writeFileSync(packageJsonPath, pkg.output);
    anyChanged = true;
  }
  results.push({ file: 'package.json', changed: pkg.changed });

  const server = syncServerJson(serverJsonPath, version);
  if (server.changed) {
    fs.writeFileSync(serverJsonPath, server.output);
    anyChanged = true;
  }
  results.push({ file: 'server.json', changed: server.changed });

  return { changed: anyChanged, results, version };
}

if (require.main === module) {
  const manifestPath = path.join(REPO_ROOT, '.release-please-manifest.json');
  const csprojPath = path.join(REPO_ROOT, 'src', 'mssql-mcp', 'mssql-mcp.csproj');
  const packageJsonPath = path.join(REPO_ROOT, 'npm', 'package.json');
  const serverJsonPath = path.join(REPO_ROOT, 'server.json');
  const { changed, results, version } = syncAllStamps({ manifestPath, csprojPath, packageJsonPath, serverJsonPath });
  if (changed) {
    console.log('Synced stamps to version ' + version + ':');
    for (const r of results) {
      if (r.changed) console.log('  ' + r.file + ' updated');
    }
  } else {
    console.log('All stamps already at version ' + version);
  }
}

module.exports = { syncAllStamps, syncCsproj, syncPackageJson, syncServerJson };
