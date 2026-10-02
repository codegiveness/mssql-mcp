'use strict';

// Unit tests for scripts/check-version-consistency.js.
// Uses FIXTURE files in scripts/test/fixtures/ — never reads real repo files.
// Run: node scripts/test/check-version-consistency.test.js
//
// Pattern matches npm/test.js: pure-function `check(name, fn)` helper, no deps.

const assert = require('assert');
const path = require('path');
const fs = require('fs');
const os = require('os');
const { execFileSync } = require('child_process');

const mod = require('../check-version-consistency.js');
const { syncAllStamps, platformPackagePaths } = require('../sync-all-stamps.js');

const FIXTURES = path.join(__dirname, 'fixtures');
const SCRIPT = path.join(__dirname, '..', 'check-version-consistency.js');

let failures = 0;
function check(name, fn) {
  try {
    fn();
    console.log('ok - ' + name);
  } catch (e) {
    failures++;
    console.error('FAIL - ' + name + ': ' + (e && e.message ? e.message : e));
  }
}

// Helper: paths to all-aligned fixtures (all at 0.5.0).
function alignedPaths() {
  return {
    manifestPath: path.join(FIXTURES, 'manifest.json'),
    csprojPath: path.join(FIXTURES, 'csproj.xml'),
    packageJsonPath: path.join(FIXTURES, 'package.json'),
    serverJsonPath: path.join(FIXTURES, 'server.json'),
  };
}

function fixturePaths(root) {
  return {
    manifestPath: path.join(root, '.release-please-manifest.json'),
    csprojPath: path.join(root, 'src', 'mssql-mcp', 'mssql-mcp.csproj'),
    packageJsonPath: path.join(root, 'npm', 'package.json'),
    serverJsonPath: path.join(root, 'server.json'),
  };
}

function withFixtureRepo(fn) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'version-stamps-'));
  try {
    const paths = fixturePaths(root);
    const originals = alignedPaths();
    for (const key of Object.keys(paths)) {
      fs.mkdirSync(path.dirname(paths[key]), { recursive: true });
      fs.copyFileSync(originals[key], paths[key]);
    }
    for (const { rid, filePath } of platformPackagePaths(paths.packageJsonPath)) {
      fs.mkdirSync(path.dirname(filePath), { recursive: true });
      fs.copyFileSync(path.join(FIXTURES, 'platforms', rid, 'package.json'), filePath);
    }
    fs.mkdirSync(path.join(root, 'scripts'));
    fs.copyFileSync(SCRIPT, path.join(root, 'scripts', 'check-version-consistency.js'));
    fs.copyFileSync(path.join(__dirname, '..', 'sync-all-stamps.js'), path.join(root, 'scripts', 'sync-all-stamps.js'));
    fn(paths, root);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}

// --- HAPPY PATH ---

check('all stamps aligned -> ok true, no errors', () => {
  const result = mod.checkVersionConsistency(alignedPaths());
  assert.strictEqual(result.ok, true);
  assert.deepStrictEqual(result.errors, []);
});

// --- CSPROJ DRIFT ---

check('csproj VersionPrefix drifted -> ok false with clear error', () => {
  const result = mod.checkVersionConsistency({
    ...alignedPaths(),
    csprojPath: path.join(FIXTURES, 'csproj-drifted.xml'),
  });
  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.errors.length, 1);
  assert.ok(result.errors[0].includes('VersionPrefix'), 'error should name VersionPrefix: ' + result.errors[0]);
  assert.ok(result.errors[0].includes('0.4.2'), 'error should show actual: ' + result.errors[0]);
  assert.ok(result.errors[0].includes('0.5.0'), 'error should show expected: ' + result.errors[0]);
});

// --- PACKAGE.JSON DRIFTS ---

check('package.json top-level version drifted -> ok false', () => {
  const result = mod.checkVersionConsistency({
    ...alignedPaths(),
    packageJsonPath: path.join(FIXTURES, 'package-version-drifted.json'),
  });
  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.errors.length, 1);
  assert.ok(result.errors[0].includes('version'), 'error should name version field: ' + result.errors[0]);
  assert.ok(result.errors[0].includes('0.4.2'), 'should show actual: ' + result.errors[0]);
  assert.ok(result.errors[0].includes('0.5.0'), 'should show expected: ' + result.errors[0]);
});

check('package.json optionalDependency drifted -> ok false', () => {
  const result = mod.checkVersionConsistency({
    ...alignedPaths(),
    packageJsonPath: path.join(FIXTURES, 'package-drifted.json'),
  });
  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.errors.length, 1);
  assert.ok(
    result.errors[0].includes('@codegiveness/mssql-mcp-linux-x64'),
    'error should name the drifted dependency: ' + result.errors[0]
  );
  assert.ok(result.errors[0].includes('0.4.2'), 'should show actual: ' + result.errors[0]);
  assert.ok(result.errors[0].includes('0.5.0'), 'should show expected: ' + result.errors[0]);
});

// --- SERVER.JSON DRIFTS ---

check('server.json top-level version drifted -> ok false', () => {
  const result = mod.checkVersionConsistency({
    ...alignedPaths(),
    serverJsonPath: path.join(FIXTURES, 'server-drifted.json'),
  });
  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.errors.length, 1);
  assert.ok(result.errors[0].includes('version'), 'should name version field: ' + result.errors[0]);
  assert.ok(result.errors[0].includes('0.4.2'), 'should show actual: ' + result.errors[0]);
  assert.ok(result.errors[0].includes('0.5.0'), 'should show expected: ' + result.errors[0]);
});

check('server.json npm package version drifted -> ok false', () => {
  const result = mod.checkVersionConsistency({
    ...alignedPaths(),
    serverJsonPath: path.join(FIXTURES, 'server-npm-drifted.json'),
  });
  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.errors.length, 1);
  assert.ok(
    result.errors[0].includes('npm'),
    'should name npm registry: ' + result.errors[0]
  );
  assert.ok(result.errors[0].includes('0.4.2'), 'should show actual: ' + result.errors[0]);
  assert.ok(result.errors[0].includes('0.5.0'), 'should show expected: ' + result.errors[0]);
});

check('server.json nuget package version drifted -> ok false', () => {
  const result = mod.checkVersionConsistency({
    ...alignedPaths(),
    serverJsonPath: path.join(FIXTURES, 'server-nuget-drifted.json'),
  });
  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.errors.length, 1);
  assert.ok(
    result.errors[0].includes('nuget'),
    'should name nuget registry: ' + result.errors[0]
  );
  assert.ok(result.errors[0].includes('0.4.2'), 'should show actual: ' + result.errors[0]);
  assert.ok(result.errors[0].includes('0.5.0'), 'should show expected: ' + result.errors[0]);
});

// --- MISSING FILES ---

check('manifest missing -> ok false with manifest file not found error', () => {
  const result = mod.checkVersionConsistency({
    ...alignedPaths(),
    manifestPath: path.join(FIXTURES, 'does-not-exist-manifest.json'),
  });
  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.errors.length, 1);
  assert.ok(
    result.errors[0].includes('manifest'),
    'should mention manifest: ' + result.errors[0]
  );
  assert.ok(
    result.errors[0].includes('not found'),
    'should say not found: ' + result.errors[0]
  );
});

check('stamp file missing -> ok false with name file not found error', () => {
  const result = mod.checkVersionConsistency({
    ...alignedPaths(),
    csprojPath: path.join(FIXTURES, 'does-not-exist.csproj'),
  });
  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.errors.length, 1);
  assert.ok(result.errors[0].includes('not found'), 'should say not found: ' + result.errors[0]);
});

// --- MALFORMED FILES ---

check('malformed JSON -> ok false with parse error detail', () => {
  const result = mod.checkVersionConsistency({
    ...alignedPaths(),
    packageJsonPath: path.join(FIXTURES, 'malformed.json'),
  });
  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.errors.length, 1);
  assert.ok(
    result.errors[0].includes('parse'),
    'should mention parse failure: ' + result.errors[0]
  );
  assert.ok(
    result.errors[0].includes('JSON'),
    'should mention JSON: ' + result.errors[0]
  );
});

check('malformed XML -> ok false (regex extraction finds no VersionPrefix)', () => {
  // The csproj parser uses regex per the spec (<VersionPrefix>...</VersionPrefix>),
  // not a full XML parser. Malformed XML therefore surfaces as "VersionPrefix not
  // found" rather than a parse-level XML error — the regex simply doesn't match.
  const result = mod.checkVersionConsistency({
    ...alignedPaths(),
    csprojPath: path.join(FIXTURES, 'malformed.xml'),
  });
  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.errors.length, 1);
  assert.ok(
    result.errors[0].includes('VersionPrefix'),
    'should name VersionPrefix (regex-based extraction): ' + result.errors[0]
  );
});

// --- REPORTS ALL DRIFTS, NOT JUST FIRST ---

check('reports all drifts across multiple files, not just first', () => {
  const result = mod.checkVersionConsistency({
    manifestPath: path.join(FIXTURES, 'manifest.json'),
    csprojPath: path.join(FIXTURES, 'csproj-drifted.xml'),
    packageJsonPath: path.join(FIXTURES, 'package-drifted.json'),
    serverJsonPath: path.join(FIXTURES, 'server-drifted.json'),
  });
  assert.strictEqual(result.ok, false);
  // csproj(1) + package optionalDep(1) + server top-level(1) = 3 drifts
  assert.strictEqual(result.errors.length, 3, 'should collect ALL drifts: ' + JSON.stringify(result.errors));
});

// --- MANIFEST MALFORMED ---

check('malformed manifest JSON -> ok false with parse error', () => {
  const result = mod.checkVersionConsistency({
    ...alignedPaths(),
    manifestPath: path.join(FIXTURES, 'malformed.json'),
  });
  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.errors.length, 1);
  assert.ok(result.errors[0].includes('parse'), 'should mention parse: ' + result.errors[0]);
  assert.ok(!result.errors[0].match(/failed to parse JSON: failed to parse JSON:/), 'should not duplicate "failed to parse JSON" phrase: ' + result.errors[0]);
});

// --- MANIFEST MISSING VERSION KEY ---

check('manifest without "." version key -> ok false with missing version error', () => {
  const noVersionManifest = path.join(FIXTURES, 'manifest-no-version.json');
  const result = mod.checkVersionConsistency({
    ...alignedPaths(),
    manifestPath: noVersionManifest,
  });
  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.errors.length, 1);
  assert.ok(
    result.errors[0].includes('version'),
    'should mention version: ' + result.errors[0]
  );
});

check('all five platform failures are reported alongside main package drift', () => {
  withFixtureRepo(paths => {
    const platformPaths = platformPackagePaths(paths.packageJsonPath);
    const contents = [
      JSON.stringify({ version: '0.5.2' }),
      JSON.stringify({ version: '0.4.0' }),
      JSON.stringify({ name: 'missing-version' }),
      '{malformed',
    ];
    for (let i = 0; i < contents.length; i++) {
      fs.writeFileSync(platformPaths[i].filePath, contents[i]);
    }
    fs.unlinkSync(platformPaths[4].filePath);
    fs.copyFileSync(path.join(FIXTURES, 'package-version-drifted.json'), paths.packageJsonPath);
    const result = mod.checkVersionConsistency(paths);
    assert.strictEqual(result.ok, false);
    assert.strictEqual(result.errors.length, 6, JSON.stringify(result.errors));
    for (const { rid } of platformPaths) {
      assert.ok(result.errors.some(error => error.includes('platforms/' + rid + '/package.json')), rid);
    }
    assert.ok(result.errors.some(error => error.includes('missing "version"')));
    assert.ok(result.errors.some(error => error.includes('failed to parse JSON')));
    assert.ok(result.errors.some(error => error.includes('file not found')));
  });
});

check('sync repairs every platform and main stamp without losing package metadata', () => {
  withFixtureRepo(paths => {
    fs.writeFileSync(paths.manifestPath, JSON.stringify({ '.': '0.5.1' }));
    const platforms = platformPackagePaths(paths.packageJsonPath);
    for (const { rid, filePath } of platforms) {
      fs.writeFileSync(filePath, JSON.stringify({
        name: '@codegiveness/mssql-mcp-' + rid, version: '0.5.0', files: ['mssql-mcp'], cpu: ['fixture-cpu'],
      }));
    }
    syncAllStamps(paths);
    assert.deepStrictEqual(mod.checkVersionConsistency(paths), { ok: true, errors: [] });
    for (const { rid, filePath } of platforms) {
      assert.deepStrictEqual(JSON.parse(fs.readFileSync(filePath, 'utf8')), {
        name: '@codegiveness/mssql-mcp-' + rid, version: '0.5.1', files: ['mssql-mcp'], cpu: ['fixture-cpu'],
      });
    }
    const before = platforms.map(({ filePath }) => fs.readFileSync(filePath, 'utf8'));
    assert.strictEqual(syncAllStamps(paths).changed, false);
    assert.deepStrictEqual(platforms.map(({ filePath }) => fs.readFileSync(filePath, 'utf8')), before);
  });
});

check('sync rejects an absent platform package instead of silently omitting it', () => {
  withFixtureRepo(paths => {
    const missing = platformPackagePaths(paths.packageJsonPath)[4].filePath;
    fs.unlinkSync(missing);
    assert.throws(() => syncAllStamps(paths), /cannot read package.json/);
  });
});

// --- CLI ENTRY POINT ---

check('CLI: exits 0 when isolated repository stamps including platforms are aligned', () => {
  withFixtureRepo((paths, root) => {
    const out = execFileSync(process.execPath, [path.join(root, 'scripts', 'check-version-consistency.js')], { encoding: 'utf8' });
    assert.ok(out.includes('Version consistency: all stamps match.'), out);
  });
});

check('CLI: exits non-zero when manifest is missing', () => {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'mvc-cli-'));
  const tmpScript = path.join(tmpDir, 'check-version-consistency.js');
  fs.copyFileSync(SCRIPT, tmpScript);
  const tmpSyncScript = path.join(tmpDir, 'sync-all-stamps.js');
  fs.copyFileSync(path.join(__dirname, '..', 'sync-all-stamps.js'), tmpSyncScript);
  let threw = false;
  let stderr = '';
  try {
    execFileSync('node', [tmpScript], { encoding: 'utf8', stdio: ['ignore', 'ignore', 'pipe'] });
  } catch (e) {
    threw = true;
    stderr = e.stderr || '';
  } finally {
    fs.unlinkSync(tmpScript);
    fs.unlinkSync(tmpSyncScript);
    fs.rmdirSync(tmpDir);
  }
  assert.strictEqual(threw, true, 'CLI should exit non-zero when manifest is missing');
  assert.ok(
    stderr.includes('manifest file not found'),
    'CLI stderr should explain the failure: ' + stderr
  );
});

if (failures > 0) {
  console.error('\n' + failures + ' test(s) failed.');
  process.exit(1);
} else {
  console.log('\nAll version-consistency tests passed.');
}
