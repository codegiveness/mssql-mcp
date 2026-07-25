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

// --- MANIFEST MISSING VERSION FIELD ---

check('manifest without version field -> ok false with missing version error', () => {
  // Use a fixture with no version key.
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

// --- CLI ENTRY POINT ---

check('CLI: exits 0 with success message when repo stamps are aligned', () => {
  const out = execFileSync('node', [SCRIPT], { encoding: 'utf8' });
  assert.ok(
    out.includes('Version consistency: all stamps match.'),
    'CLI should print success message: ' + out
  );
});

check('CLI: exits non-zero when manifest is missing', () => {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'mvc-cli-'));
  const tmpScript = path.join(tmpDir, 'check-version-consistency.js');
  fs.copyFileSync(SCRIPT, tmpScript);
  let threw = false;
  let stderr = '';
  try {
    execFileSync('node', [tmpScript], { encoding: 'utf8', stdio: ['ignore', 'ignore', 'pipe'] });
  } catch (e) {
    threw = true;
    stderr = e.stderr || '';
  } finally {
    fs.unlinkSync(tmpScript);
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
