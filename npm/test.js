'use strict';

// Smoke test for the postinstall-independent shim (npm/bin/mssql-mcp.js).
// Run with: node npm/test.js
// Verifies:
//   1. The shim parses without syntax errors and exposes the expected pure functions
//   2. RID mapping returns the expected RID for known (platform, arch) pairs
//   3. archiveExt, parseChecksumFile, classifyDownloadError, sha256Hex behave correctly

const assert = require('assert');

// Load the shim module. require() executes the top-level code but does NOT
// run main() (guarded by `require.main === module`), so it's safe to require.
function loadShimModule() {
  return require('./bin/mssql-mcp.js');
}

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

check('shim parses without syntax errors and exports functions', () => {
  const mod = loadShimModule();
  assert.strictEqual(typeof mod.ridFor, 'function');
  assert.strictEqual(typeof mod.archiveExt, 'function');
  assert.strictEqual(typeof mod.parseChecksumFile, 'function');
  assert.strictEqual(typeof mod.classifyDownloadError, 'function');
  assert.strictEqual(typeof mod.sha256Hex, 'function');
});

check('ridFor maps known platforms', () => {
  const mod = loadShimModule();
  assert.strictEqual(mod.ridFor('linux', 'x64'), 'linux-x64');
  assert.strictEqual(mod.ridFor('linux', 'arm64'), 'linux-arm64');
  assert.strictEqual(mod.ridFor('darwin', 'x64'), 'osx-x64');
  assert.strictEqual(mod.ridFor('darwin', 'arm64'), 'osx-arm64');
  assert.strictEqual(mod.ridFor('win32', 'x64'), 'win-x64');
});

check('ridFor returns null for unsupported platforms', () => {
  const mod = loadShimModule();
  assert.strictEqual(mod.ridFor('aix', 'x64'), null);
  assert.strictEqual(mod.ridFor('sunos', 'arm64'), null);
  assert.strictEqual(mod.ridFor('freebsd', 'x64'), null);
  assert.strictEqual(mod.ridFor('linux', 'ia32'), null);
});

check('archiveExt returns .zip for win-x64 and .tar.gz elsewhere', () => {
  const mod = loadShimModule();
  assert.strictEqual(mod.archiveExt('win-x64'), '.zip');
  assert.strictEqual(mod.archiveExt('linux-x64'), '.tar.gz');
  assert.strictEqual(mod.archiveExt('osx-arm64'), '.tar.gz');
});

check('parseChecksumFile accepts bare and sha256sum formats', () => {
  const mod = loadShimModule();
  const bare = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855';
  const paired = bare + '  mssql-mcp-0.1.0-linux-x64.tar.gz';
  const star = bare + ' *mssql-mcp-0.1.0-linux-x64.tar.gz';
  assert.strictEqual(mod.parseChecksumFile(bare), bare);
  assert.strictEqual(mod.parseChecksumFile(paired), bare);
  assert.strictEqual(mod.parseChecksumFile(star), bare);
});

check('parseChecksumFile throws on invalid input', () => {
  const mod = loadShimModule();
  assert.throws(() => mod.parseChecksumFile('not a checksum'), /no 64-hex SHA256 token/);
});

check('sha256Hex computes correct hash', () => {
  const mod = loadShimModule();
  // SHA256 of empty string
  assert.strictEqual(
    mod.sha256Hex(Buffer.alloc(0)),
    'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
  );
  // SHA256 of "hello"
  assert.strictEqual(
    mod.sha256Hex(Buffer.from('hello', 'utf8')),
    '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824'
  );
});

check('classifyDownloadError classifies HTTP 404 as release not published', () => {
  const mod = loadShimModule();
  const err = new Error('HTTP 404 for https://github.com/.../mssql-mcp-0.1.0-linux-x64.tar.gz');
  assert.strictEqual(mod.classifyDownloadError(err), 'release not published yet');
});

check('classifyDownloadError classifies ECONNRESET as network or proxy blocked', () => {
  const mod = loadShimModule();
  const err = new Error('connect ECONNRESET');
  err.code = 'ECONNRESET';
  assert.strictEqual(mod.classifyDownloadError(err), 'network or proxy blocked');
});

check('classifyDownloadError classifies ENOTFOUND as network or proxy blocked', () => {
  const mod = loadShimModule();
  const err = new Error('getaddrinfo ENOTFOUND github.com');
  err.code = 'ENOTFOUND';
  assert.strictEqual(mod.classifyDownloadError(err), 'network or proxy blocked');
});

check('classifyDownloadError classifies ECONNREFUSED as network or proxy blocked', () => {
  const mod = loadShimModule();
  const err = new Error('connect ECONNREFUSED 127.0.0.1:443');
  err.code = 'ECONNREFUSED';
  assert.strictEqual(mod.classifyDownloadError(err), 'network or proxy blocked');
});

check('classifyDownloadError classifies timeout as timed out', () => {
  const mod = loadShimModule();
  const err = new Error('timeout downloading https://github.com/.../archive');
  assert.strictEqual(mod.classifyDownloadError(err), 'timed out');
});

check('classifyDownloadError falls back to download failed for unknown errors', () => {
  const mod = loadShimModule();
  assert.strictEqual(mod.classifyDownloadError(new Error('something weird')), 'download failed');
  assert.strictEqual(mod.classifyDownloadError(new Error('HTTP 500 for ...')), 'download failed');
});

if (failures > 0) {
  console.error('\n' + failures + ' test(s) failed.');
  process.exit(1);
} else {
  console.log('\nAll smoke tests passed.');
}
