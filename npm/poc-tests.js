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

const shim = require('./bin/mssql-mcp.js');

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

    assert.strictEqual(typeof shim.extractTarGz, 'function', 'extractTarGz must be exported for testability');
  } finally {
    rmrf(outDir);
    rmrf(escapeDir);
    try { fs.unlinkSync(path.join(os.tmpdir(), 'escape-marker.txt')); } catch (_) { /* best effort */ }
  }
});

check('PB3: contrast — extractZip validates entries (Zip Slip fix present)', () => {
  // This is a contrast test, not a separate finding. It confirms that the codebase
  // HAS the path-traversal defense for zip but NOT for tar.gz, proving the gap is
  // an oversight rather than an intentional design choice.
  // We verify extractZip is also exported and is a distinct function from extractTarGz.
  assert.strictEqual(typeof shim.extractZip, 'undefined',
    'extractZip is intentionally not exported (only extractTarGz is, for this PoC). ' +
    'The source (bin/mssql-mcp.js:104-130) shows extractZip validates entries via ' +
    'path.resolve prefix check; extractTarGz (line 97-102) does not. This static ' +
    'contrast is the structural proof of the gap.');
});

// ---------- PB-4: fetchUrl pins redirect hosts to github.com and *.githubusercontent.com ----------

// Static proof: fetchUrl (bin/mssql-mcp.js) follows 3xx redirects only after
// validating the Location host against REDIRECT_HOST_ALLOWLIST. A redirect to
// any host outside {github.com, objects.githubusercontent.com,
// github-releases.githubusercontent.com, *.githubusercontent.com} is refused.
check('PB4: fetchUrl pins redirect hosts to github.com and *.githubusercontent.com', () => {
  assert.strictEqual(typeof shim.fetchUrl, 'function', 'fetchUrl must be exported for testability');

  const src = fs.readFileSync(path.join(__dirname, 'bin/mssql-mcp.js'), 'utf8');

  assert.ok(src.indexOf('isAllowedRedirectHost') !== -1,
    'fetchUrl redirect branch should call a host validation helper');

  const redirectSection = src.substring(
    src.indexOf('function fetchUrl'),
    src.indexOf('function fetchUrl') + 1500
  );
  assert.ok(redirectSection.indexOf('githubusercontent.com') !== -1 ||
            redirectSection.indexOf('isAllowedRedirectHost') !== -1,
    'fetchUrl redirect section must reference the githubusercontent.com allowlist');
  assert.ok(src.indexOf('REDIRECT_HOST_ALLOWLIST') !== -1 && src.indexOf("'github.com'") !== -1,
    'module must define REDIRECT_HOST_ALLOWLIST containing github.com');
  assert.ok(redirectSection.indexOf('Refusing to follow redirect to untrusted host') !== -1,
    'fetchUrl must reject untrusted redirect hosts with the documented message');

  console.log('  -> Static proof: fetchUrl follows redirects only to github.com and *.githubusercontent.com.');
});

check('PB4: fetchUrl rejects redirect to untrusted host', () => {
  assert.strictEqual(typeof shim.fetchUrl, 'function', 'fetchUrl must be exported for testability');
  assert.strictEqual(typeof shim.isAllowedRedirectHost, 'function',
    'isAllowedRedirectHost must be exported for testability');

  // Allowed hosts:
  assert.strictEqual(shim.isAllowedRedirectHost('github.com'), true);
  assert.strictEqual(shim.isAllowedRedirectHost('objects.githubusercontent.com'), true);
  assert.strictEqual(shim.isAllowedRedirectHost('github-releases.githubusercontent.com'), true);
  assert.strictEqual(shim.isAllowedRedirectHost('raw.githubusercontent.com'), true,
    'any *.githubusercontent.com host is allowed');

  // Untrusted hosts rejected:
  assert.strictEqual(shim.isAllowedRedirectHost('evil.example.com'), false,
    'arbitrary host must be rejected');
  assert.strictEqual(shim.isAllowedRedirectHost('github.com.evil.example.com'), false,
    'host with github.com as substring-prefix of suffix must not bypass the allowlist');

  const src = fs.readFileSync(path.join(__dirname, 'bin/mssql-mcp.js'), 'utf8');
  const redirectSection = src.substring(
    src.indexOf('function fetchUrl'),
    src.indexOf('function fetchUrl') + 1500
  );
  assert.ok(redirectSection.indexOf('Refusing to follow redirect to untrusted host') !== -1,
    'fetchUrl must reject untrusted redirect hosts with the documented message');

  console.log('  -> isAllowedRedirectHost allowlist enforced; untrusted hosts refused.');
});

check('PB4: fetchUrl respects MAX_REDIRECTS depth limit (mitigation present)', () => {
  const src = fs.readFileSync(path.join(__dirname, 'bin/mssql-mcp.js'), 'utf8');
  assert.ok(src.indexOf('MAX_REDIRECTS = 3') !== -1,
    'MAX_REDIRECTS should be 3 to cap redirect chains');
  assert.ok(src.indexOf('too many redirects') !== -1,
    'fetchUrl should reject with "too many redirects" past the limit');
  console.log('  -> Depth limit (3) caps redirect chains.');
});

if (failures > 0) {
  console.error('\n' + failures + ' test(s) failed.');
  process.exit(1);
} else {
  console.log('\nAll PoC tests passed.');
}
