'use strict';

// PoC tests for npm shim security findings (poc-engineer-b, Phase 3).
// Verifies:
//   PB-3: extractTarGz has NO path traversal validation (unlike extractZip).
//   PB-4: fetchUrl follows redirects to ANY https host (no host pinning).
//
// Each test is self-contained and safe to run: it operates in a temp dir, uses
// toy fixtures, and cleans up after itself.

const assert = require('assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');
const https = require('https');
const { EventEmitter } = require('events');

const shim = require('./bin/mssql-mcp.js');

let failures = 0;
let checks = Promise.resolve();
function check(name, fn) {
  checks = checks.then(async () => {
    try {
      await fn();
      console.log('ok - ' + name);
    } catch (e) {
      failures++;
      console.error('FAIL - ' + name + ': ' + (e && e.message ? e.message : e));
    }
  });
}

function makeTempDir() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'mssql-mcp-poc-'));
  return dir;
}

function rmrf(p) {
  try { fs.rmSync(p, { recursive: true, force: true }); } catch (_) { /* best effort */ }
}

// ---------- PB-3: extractTarGz path traversal (inverted: must reject) ----------

// Build a tarball with a `../escape-marker.txt` entry, extract it via extractTarGz,
// and assert extractTarGz REJECTS the malicious archive before extraction.
//
// extractTarGz now validates entries BEFORE extraction (mirrors extractZip):
//   - rejects entries containing `..` or starting with `/`
//   - rejects symlink/hardlink entries (type flags `l` / `h`)
//
// The defense is app-level: regardless of tar's default `--absolute-names` behavior,
// the application must refuse traversal entries explicitly (defense-in-depth).
check('PB3: extractTarGz rejects crafted tarball with traversal entry', () => {
  const outDir = makeTempDir();
  const escapeDir = makeTempDir();
  try {
    const stagingDir = makeTempDir();
    const payload = 'PB3-ESCAPE-MARKER';
    const payloadPath = path.join(stagingDir, 'escape-marker.txt');
    fs.writeFileSync(payloadPath, payload);

    const archivePath = path.join(stagingDir, 'crafted.tar.gz');
    const r = spawnSync('tar', [
      '-C', stagingDir,
      '--transform', 's|escape-marker.txt|../escape-marker.txt|',
      '-czf', archivePath,
      'escape-marker.txt',
    ], { encoding: 'utf8' });
    if (r.status !== 0) {
      throw new Error('tar create failed: ' + r.stderr);
    }

    const list = spawnSync('tar', ['-tzf', archivePath], { encoding: 'utf8' });
    assert.ok(
      list.stdout.indexOf('../escape-marker.txt') !== -1,
      'archive should contain a ../ entry; got: ' + list.stdout
    );

    // extractTarGz MUST throw on the malicious archive.
    assert.throws(
      () => shim.extractTarGz(archivePath, outDir),
      /Refusing to extract tar entry outside target directory/,
      'extractTarGz must reject archives with traversal entries'
    );

    // Confirm no file escaped outside outDir.
    const escapeTarget = path.join(path.dirname(outDir), 'escape-marker.txt');
    assert.strictEqual(fs.existsSync(escapeTarget), false,
      'no file should escape outside outDir after rejected extraction');
    assert.strictEqual(fs.existsSync(path.join(outDir, 'escape-marker.txt')), false,
      'no file should be written inside outDir either');

  } finally {
    rmrf(outDir);
    rmrf(escapeDir);
    try { fs.unlinkSync(path.join(os.tmpdir(), 'escape-marker.txt')); } catch (_) { /* best effort */ }
  }
});

// ---------- PB-4: exercise redirect rejection and depth boundaries ----------

async function withRedirects(locations, fn) {
  const originalGet = https.get;
  const requested = [];
  https.get = (url, callback) => {
    requested.push(url);
    const location = locations.shift();
    const response = new EventEmitter();
    response.statusCode = location ? 302 : 200;
    response.headers = location ? { location } : {};
    response.resume = () => {};
    const request = new EventEmitter();
    request.setTimeout = () => request;
    queueMicrotask(() => {
      callback(response);
      if (!location) {
        response.emit('data', Buffer.from('verified-archive'));
        response.emit('end');
      }
    });
    return request;
  };
  try {
    await fn(requested);
  } finally {
    https.get = originalGet;
  }
}

check('PB4: untrusted redirects are rejected before another connection', async () => {
  const start = 'https://github.com/codegiveness/mssql-mcp/archive';
  for (const host of ['evil.example.com', 'github.com.evil.example.com']) {
    await withRedirects(['https://' + host + '/payload'], async (requested) => {
      await assert.rejects(shim.fetchUrl(start), /untrusted host/);
      assert.deepStrictEqual(requested, [start]);
    });
  }
});

check('PB4: allowed redirects return bytes at the limit and reject longer chains', async () => {
  const start = 'https://github.com/codegiveness/mssql-mcp/archive';
  const redirects = [
    'https://objects.githubusercontent.com/one',
    'https://github-releases.githubusercontent.com/two',
    'https://raw.githubusercontent.com/three',
  ];
  await withRedirects([...redirects], async (requested) => {
    assert.deepStrictEqual(await shim.fetchUrl(start), Buffer.from('verified-archive'));
    assert.deepStrictEqual(requested, [start, ...redirects]);
  });
  await withRedirects([...redirects, start], async (requested) => {
    await assert.rejects(shim.fetchUrl(start), /too many redirects/);
    assert.deepStrictEqual(requested, [start, ...redirects]);
  });
});


// ---------- PB5: cache poisoning — re-verify sha256 on cache hit ----------

// Setup helper: build a fake cache dir at <cacheRoot>/<version>/<rid>/ containing
// the binary + (optional) sidecar. Returns { cacheDir, binaryPath, sidecarPath }.
function buildFakeCache(cacheRoot, version, rid, binaryContent, sidecarHash) {
  const binaryName = process.platform === 'win32' ? 'mssql-mcp.exe' : 'mssql-mcp';
  const cacheDir = path.join(cacheRoot, version, rid);
  fs.mkdirSync(cacheDir, { recursive: true });
  const binaryPath = path.join(cacheDir, binaryName);
  fs.writeFileSync(binaryPath, binaryContent);
  const sidecarPath = binaryPath + '.sha256';
  if (sidecarHash !== undefined) {
    fs.writeFileSync(sidecarPath, sidecarHash + '\n');
  }
  return { cacheDir, binaryPath, sidecarPath };
}

const PB5_VERSION = require('./package.json').version;
const PB5_RID = shim.ridFor(process.platform, process.arch) || 'linux-x64';

check('PB5: mismatched sha256 sidecar triggers cache miss + purges binary and sidecar', () => {
  assert.strictEqual(typeof shim.resolveCachedBinary, 'function',
    'resolveCachedBinary must be exported for testability');
  const cacheRoot = makeTempDir();
  try {
    const binaryContent = Buffer.from('fake-binary-bytes');
    const wrongHash = '0'.repeat(64);
    const { binaryPath, sidecarPath } = buildFakeCache(cacheRoot, PB5_VERSION, PB5_RID, binaryContent, wrongHash);
    assert.strictEqual(fs.existsSync(binaryPath), true);
    assert.strictEqual(fs.existsSync(sidecarPath), true);

    const result = shim.resolveCachedBinary(PB5_RID, cacheRoot);
    assert.strictEqual(result, null, 'mismatched sha256 must return null (cache miss)');
    assert.strictEqual(fs.existsSync(binaryPath), false,
      'tampered binary must be deleted on mismatch');
    assert.strictEqual(fs.existsSync(sidecarPath), false,
      'mismatched sidecar must be deleted on mismatch');
  } finally {
    rmrf(cacheRoot);
  }
});

check('PB5: missing sha256 sidecar triggers cache miss (no trust without recorded checksum)', () => {
  const cacheRoot = makeTempDir();
  try {
    const { binaryPath, sidecarPath } = buildFakeCache(cacheRoot, PB5_VERSION, PB5_RID, Buffer.from('x'), undefined);
    assert.strictEqual(fs.existsSync(binaryPath), true);
    assert.strictEqual(fs.existsSync(sidecarPath), false);

    const result = shim.resolveCachedBinary(PB5_RID, cacheRoot);
    assert.strictEqual(result, null, 'missing sidecar must return null (cache miss)');
    assert.strictEqual(fs.existsSync(binaryPath), true,
      'binary is NOT deleted when sidecar is merely missing (no tampering proven)');
  } finally {
    rmrf(cacheRoot);
  }
});

check('PB5: matching sha256 sidecar returns cached path (verified cache hit)', () => {
  const cacheRoot = makeTempDir();
  try {
    const binaryContent = Buffer.from('legit-binary-bytes');
    const correctHash = shim.sha256Hex(binaryContent);
    const { binaryPath } = buildFakeCache(cacheRoot, PB5_VERSION, PB5_RID, binaryContent, correctHash);

    const result = shim.resolveCachedBinary(PB5_RID, cacheRoot);
    assert.strictEqual(result, binaryPath, 'matching sha256 must return the cached binary path');
    assert.strictEqual(fs.existsSync(binaryPath), true, 'verified binary must remain in place');
  } finally {
    rmrf(cacheRoot);
  }
});

check('PB5: no cached binary returns null (baseline cache miss)', () => {
  const cacheRoot = makeTempDir();
  try {
    const result = shim.resolveCachedBinary(PB5_RID, cacheRoot);
    assert.strictEqual(result, null, 'empty cache must return null');
  } finally {
    rmrf(cacheRoot);
  }
});

checks.then(() => {
  if (failures > 0) {
    console.error('\n' + failures + ' test(s) failed.');
    process.exitCode = 1;
  } else {
    console.log('\nAll PoC tests passed.');
  }
});
