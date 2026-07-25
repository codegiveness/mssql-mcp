'use strict';

// Version-consistency check.
//
// Reads the canonical version from the release-please manifest (the single
// source of truth) and verifies every other version stamp matches it:
//
//   - src/mssql-mcp/mssql-mcp.csproj           <VersionPrefix>...</VersionPrefix>
//   - npm/package.json                          "version" + every "optionalDependencies" entry
//   - server.json                               top-level "version" + packages[].version (npm + nuget)
//
// Collects ALL drifts — does not stop at the first. Missing files and parse
// errors are reported per-file and never crash the check.
//
// Run: node scripts/check-version-consistency.js
// Exits 0 if all stamps match, 1 otherwise.
//
// Exported for unit tests: checkVersionConsistency({ manifestPath, csprojPath,
// packageJsonPath, serverJsonPath }) -> { ok: boolean, errors: string[] }.

const fs = require('fs');
const path = require('path');

// Extract <VersionPrefix>X</VersionPrefix> from csproj XML via regex.
// Returns the version string, or null if the element is absent.
function extractVersionPrefix(xml) {
  const m = xml.match(/<VersionPrefix>\s*([^<\s]+)\s*<\/VersionPrefix>/);
  return m ? m[1] : null;
}

// Read & JSON.parse a file. Throws { kind: 'parse', message } on parse failure,
// { kind: 'missing' } on ENOENT — both caught by the caller.
function readJson(filePath) {
  let raw;
  try {
    raw = fs.readFileSync(filePath, 'utf8');
  } catch (e) {
    if (e.code === 'ENOENT') {
      const err = new Error('file not found: ' + filePath);
      err.kind = 'missing';
      throw err;
    }
    throw e;
  }
  try {
    return JSON.parse(raw);
  } catch (e) {
    const err = new Error('failed to parse JSON: ' + e.message);
    err.kind = 'parse';
    throw err;
  }
}

// Read a text file (for XML / csproj). Throws { kind: 'missing' } on ENOENT.
function readText(filePath) {
  try {
    return fs.readFileSync(filePath, 'utf8');
  } catch (e) {
    if (e.code === 'ENOENT') {
      const err = new Error('file not found: ' + filePath);
      err.kind = 'missing';
      throw err;
    }
    throw e;
  }
}

function checkVersionConsistency(opts) {
  const errors = [];
  const manifestPath = opts.manifestPath;
  const csprojPath = opts.csprojPath;
  const packageJsonPath = opts.packageJsonPath;
  const serverJsonPath = opts.serverJsonPath;

  // 1. Read manifest (single source of truth).
  let manifestVersion = null;
  try {
    const manifest = readJson(manifestPath);
    const v = manifest['.'];
    if (typeof v !== 'string' || v.length === 0) {
      errors.push('manifest: missing "." version key in ' + manifestPath);
    } else {
      manifestVersion = v;
    }
  } catch (e) {
    if (e.kind === 'missing') {
      errors.push('manifest file not found: ' + manifestPath);
    } else if (e.kind === 'parse') {
      errors.push('manifest: failed to parse JSON: ' + e.message.replace(/^.*failed to parse JSON: /, ''));
    } else {
      throw e;
    }
  }

  // No point checking stamps if we don't know the canonical version.
  if (manifestVersion === null) {
    return { ok: false, errors: errors };
  }

  // 2. Check csproj <VersionPrefix>.
  try {
    const xml = readText(csprojPath);
    const v = extractVersionPrefix(xml);
    if (v === null) {
      errors.push('csproj: <VersionPrefix> not found in ' + csprojPath);
    } else if (v !== manifestVersion) {
      errors.push('csproj: <VersionPrefix> is ' + v + ', expected ' + manifestVersion);
    }
  } catch (e) {
    if (e.kind === 'missing') {
      errors.push('csproj file not found: ' + csprojPath);
    } else {
      // Malformed XML — extractVersionPrefix returns null when no element found,
      // so a parse-level XML error surfaces here only if readText itself failed
      // in an unexpected way. Treat as parse error for the malformed-XML test case.
      errors.push('csproj: failed to parse XML: ' + (e.message || e));
    }
  }

  // 3. Check package.json version + optionalDependencies.
  try {
    const pkg = readJson(packageJsonPath);
    if (typeof pkg.version !== 'string') {
      errors.push('package.json: missing "version" field');
    } else if (pkg.version !== manifestVersion) {
      errors.push('package.json: version is ' + pkg.version + ', expected ' + manifestVersion);
    }
    if (pkg.optionalDependencies && typeof pkg.optionalDependencies === 'object') {
      for (const [dep, ver] of Object.entries(pkg.optionalDependencies)) {
        if (ver !== manifestVersion) {
          errors.push(
            'package.json: optionalDependency "' + dep + '" is ' + ver + ', expected ' + manifestVersion
          );
        }
      }
    }
  } catch (e) {
    if (e.kind === 'missing') {
      errors.push('package.json file not found: ' + packageJsonPath);
    } else if (e.kind === 'parse') {
      errors.push('package.json: failed to parse JSON: ' + e.message.replace(/^.*failed to parse JSON: /, ''));
    } else {
      throw e;
    }
  }

  // 4. Check server.json: top-level version + packages[] (npm + nuget).
  try {
    const srv = readJson(serverJsonPath);
    if (typeof srv.version !== 'string') {
      errors.push('server.json: missing top-level "version" field');
    } else if (srv.version !== manifestVersion) {
      errors.push('server.json: top-level version is ' + srv.version + ', expected ' + manifestVersion);
    }
    if (Array.isArray(srv.packages)) {
      for (const pkg of srv.packages) {
        const reg = pkg.registryType || 'unknown';
        const ver = pkg.version;
        if (typeof ver !== 'string') {
          errors.push('server.json: ' + reg + ' package missing "version" field');
        } else if (ver !== manifestVersion) {
          errors.push(
            'server.json: ' + reg + ' package version is ' + ver + ', expected ' + manifestVersion
          );
        }
      }
    } else {
      errors.push('server.json: missing "packages" array');
    }
  } catch (e) {
    if (e.kind === 'missing') {
      errors.push('server.json file not found: ' + serverJsonPath);
    } else if (e.kind === 'parse') {
      errors.push('server.json: failed to parse JSON: ' + e.message.replace(/^.*failed to parse JSON: /, ''));
    } else {
      throw e;
    }
  }

  return { ok: errors.length === 0, errors: errors };
}

module.exports = { checkVersionConsistency };

// When run directly: use default repo-root paths and exit 0/1.
if (require.main === module) {
  const repoRoot = path.join(__dirname, '..');
  const result = checkVersionConsistency({
    manifestPath: path.join(repoRoot, '.release-please-manifest.json'),
    csprojPath: path.join(repoRoot, 'src', 'mssql-mcp', 'mssql-mcp.csproj'),
    packageJsonPath: path.join(repoRoot, 'npm', 'package.json'),
    serverJsonPath: path.join(repoRoot, 'server.json'),
  });

  if (result.ok) {
    console.log('Version consistency: all stamps match.');
    process.exit(0);
  } else {
    console.error('Version consistency check FAILED:');
    for (const e of result.errors) {
      console.error('  - ' + e);
    }
    process.exit(1);
  }
}
