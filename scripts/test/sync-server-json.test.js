'use strict';

// Unit tests for scripts/sync-server-json.js.
// Verifies that the script reads the manifest version and writes all three
// version fields in server.json (top-level, npm package, nuget package),
// is idempotent, touches ONLY the 3 version fields, and surfaces clear
// errors for missing/malformed inputs.
//
// Tests use fixture files only — never the real repo server.json.
// Run: node scripts/test/sync-server-json.test.js

const assert = require('assert');
const fs = require('fs');
const os = require('os');
const path = require('path');

const { syncServerJson } = require('../sync-server-json.js');

const FIXTURES = path.join(__dirname, 'fixtures');

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

// Copy a fixture to a temp file so we can mutate it without touching fixtures.
function copyFixture(name) {
  const tmp = path.join(
    os.tmpdir(),
    'sync-server-json-test-' + process.pid + '-' + name
  );
  fs.copyFileSync(path.join(FIXTURES, name), tmp);
  return tmp;
}

function readVersionLines(filePath) {
  // Returns the value of every top-level and packages[].version field.
  const obj = JSON.parse(fs.readFileSync(filePath, 'utf8'));
  return {
    top: obj.version,
    npm: obj.packages[0].version,
    nuget: obj.packages[1].version,
  };
}

check('write: syncs all three version fields from 0.4.2 to 0.5.0', () => {
  const manifestPath = path.join(FIXTURES, 'manifest.json');
  const serverPath = copyFixture('server-input.json');
  try {
    const before = readVersionLines(serverPath);
    assert.strictEqual(before.top, '0.4.2');
    assert.strictEqual(before.npm, '0.4.2');
    assert.strictEqual(before.nuget, '0.4.2');

    const { changed, output } = syncServerJson({ manifestPath, serverJsonPath: serverPath });

    assert.strictEqual(changed, true, 'changed must be true when versions differ');
    assert.ok(output.endsWith('\n'), 'output must end with trailing newline');

    fs.writeFileSync(serverPath, output);
    const after = readVersionLines(serverPath);
    assert.strictEqual(after.top, '0.5.0');
    assert.strictEqual(after.npm, '0.5.0');
    assert.strictEqual(after.nuget, '0.5.0');
  } finally {
    fs.rmSync(serverPath, { force: true });
  }
});

check('idempotency: running sync twice produces byte-identical output', () => {
  const manifestPath = path.join(FIXTURES, 'manifest.json');
  const serverPath = copyFixture('server-input.json');
  try {
    const r1 = syncServerJson({ manifestPath, serverJsonPath: serverPath });
    fs.writeFileSync(serverPath, r1.output);
    const r2 = syncServerJson({ manifestPath, serverJsonPath: serverPath });

    assert.strictEqual(r2.changed, false, 'second run must report changed=false');
    assert.strictEqual(r1.output, r2.output, 'output must be byte-identical');
  } finally {
    fs.rmSync(serverPath, { force: true });
  }
});

check('no-touch: only the three version fields change', () => {
  const manifestPath = path.join(FIXTURES, 'manifest.json');
  const serverPath = copyFixture('server-input.json');
  try {
    const beforeRaw = fs.readFileSync(serverPath, 'utf8');
    const before = JSON.parse(beforeRaw);

    const { output } = syncServerJson({ manifestPath, serverJsonPath: serverPath });
    const after = JSON.parse(output);

    // Versions changed.
    assert.notStrictEqual(after.version, before.version);
    assert.notStrictEqual(after.packages[0].version, before.packages[0].version);
    assert.notStrictEqual(after.packages[1].version, before.packages[1].version);

    // Every other field is unchanged.
    assert.strictEqual(after.$schema, before.$schema);
    assert.strictEqual(after.name, before.name);
    assert.strictEqual(after.title, before.title);
    assert.strictEqual(after.description, before.description);
    assert.deepStrictEqual(after.repository, before.repository);

    assert.strictEqual(after.packages[0].registryType, before.packages[0].registryType);
    assert.strictEqual(after.packages[0].registryBaseUrl, before.packages[0].registryBaseUrl);
    assert.strictEqual(after.packages[0].identifier, before.packages[0].identifier);
    assert.deepStrictEqual(after.packages[0].transport, before.packages[0].transport);
    assert.deepStrictEqual(after.packages[0].environmentVariables, before.packages[0].environmentVariables);

    assert.strictEqual(after.packages[1].registryType, before.packages[1].registryType);
    assert.strictEqual(after.packages[1].registryBaseUrl, before.packages[1].registryBaseUrl);
    assert.strictEqual(after.packages[1].identifier, before.packages[1].identifier);
    assert.deepStrictEqual(after.packages[1].transport, before.packages[1].transport);
    assert.deepStrictEqual(after.packages[1].environmentVariables, before.packages[1].environmentVariables);

    // Line count unchanged (3 lines changed in place, none added/removed).
    const beforeLines = beforeRaw.split('\n').length;
    const afterLines = output.split('\n').length;
    assert.strictEqual(afterLines, beforeLines, 'line count must not change');
  } finally {
    fs.rmSync(serverPath, { force: true });
  }
});

check('no-touch: already-synced fixture reports changed=false with zero line changes', () => {
  const manifestPath = path.join(FIXTURES, 'manifest.json');
  const serverPath = copyFixture('server-already-synced.json');
  try {
    const beforeRaw = fs.readFileSync(serverPath, 'utf8');
    const { changed, output } = syncServerJson({ manifestPath, serverJsonPath: serverPath });

    assert.strictEqual(changed, false, 'already-synced must report changed=false');
    assert.strictEqual(output, beforeRaw, 'already-synced output must be byte-identical to input');
  } finally {
    fs.rmSync(serverPath, { force: true });
  }
});

check('error: manifest missing returns a clear error', () => {
  const serverPath = copyFixture('server-input.json');
  try {
    assert.throws(
      () => syncServerJson({ manifestPath: '/nonexistent/manifest.json', serverJsonPath: serverPath }),
      /manifest/i
    );
  } finally {
    fs.rmSync(serverPath, { force: true });
  }
});

check('error: server.json missing returns a clear error', () => {
  const manifestPath = path.join(FIXTURES, 'manifest.json');
  assert.throws(
    () => syncServerJson({ manifestPath, serverJsonPath: '/nonexistent/server.json' }),
    /server\.json/i
  );
});

check('error: manifest malformed returns a clear error', () => {
  const tmpManifest = path.join(os.tmpdir(), 'sync-bad-manifest-' + process.pid + '.json');
  const serverPath = copyFixture('server-input.json');
  try {
    fs.writeFileSync(tmpManifest, '{ not valid json');
    assert.throws(
      () => syncServerJson({ manifestPath: tmpManifest, serverJsonPath: serverPath }),
      /manifest/i
    );
  } finally {
    fs.rmSync(tmpManifest, { force: true });
    fs.rmSync(serverPath, { force: true });
  }
});

check('error: server.json malformed returns a clear error', () => {
  const manifestPath = path.join(FIXTURES, 'manifest.json');
  const tmpServer = path.join(os.tmpdir(), 'sync-bad-server-' + process.pid + '.json');
  try {
    fs.writeFileSync(tmpServer, '{ not valid json');
    assert.throws(
      () => syncServerJson({ manifestPath, serverJsonPath: tmpServer }),
      /server\.json/i
    );
  } finally {
    fs.rmSync(tmpServer, { force: true });
  }
});

if (failures > 0) {
  console.error('\n' + failures + ' test(s) failed.');
  process.exit(1);
} else {
  console.log('\nAll tests passed.');
}
