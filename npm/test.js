'use strict';

// Smoke test for npm/install.js. Run with: node npm/test.js
// Verifies:
//   1. install.js parses without syntax errors
//   2. RID mapping returns the expected RID for known (platform, arch) pairs
//   3. install.js exposes the RID mapping as a function we can call directly

const assert = require('assert');
const path = require('path');
const vm = require('vm');

function loadInstallModule() {
  const source = require('fs').readFileSync(path.join(__dirname, 'install.js'), 'utf8');
  // Expose stubs for the modules install.js requires at load time so we can
  // require it without triggering postinstall. We don't run main() — we just
  // want to assert the file parses and exposes ridFor.
  const sandbox = {
    require: (mod) => {
      if (mod === 'https' || mod === 'crypto' || mod === 'fs' || mod === 'os' || mod === 'path' || mod === 'child_process') {
        return require(mod);
      }
      return {};
    },
    console,
    process,
    __dirname: __dirname,
    module: { exports: {} },
  };
  sandbox.exports = sandbox.module.exports;
  vm.runInNewContext(source, sandbox, { filename: 'install.js' });
  return sandbox.module.exports;
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

check('install.js parses without syntax errors', () => {
  loadInstallModule();
});

check('ridFor maps known platforms', () => {
  const mod = loadInstallModule();
  assert.strictEqual(mod.ridFor('linux', 'x64'), 'linux-x64');
  assert.strictEqual(mod.ridFor('linux', 'arm64'), 'linux-arm64');
  assert.strictEqual(mod.ridFor('darwin', 'x64'), 'osx-x64');
  assert.strictEqual(mod.ridFor('darwin', 'arm64'), 'osx-arm64');
  assert.strictEqual(mod.ridFor('win32', 'x64'), 'win-x64');
});

check('ridFor returns null for unsupported platforms', () => {
  const mod = loadInstallModule();
  assert.strictEqual(mod.ridFor('aix', 'x64'), null);
  assert.strictEqual(mod.ridFor('sunos', 'arm64'), null);
  assert.strictEqual(mod.ridFor('freebsd', 'x64'), null);
  assert.strictEqual(mod.ridFor('linux', 'ia32'), null);
});

check('archiveExt returns .zip for win-x64 and .tar.gz elsewhere', () => {
  const mod = loadInstallModule();
  assert.strictEqual(mod.archiveExt('win-x64'), '.zip');
  assert.strictEqual(mod.archiveExt('linux-x64'), '.tar.gz');
  assert.strictEqual(mod.archiveExt('osx-arm64'), '.tar.gz');
});

check('parseChecksumFile accepts bare and sha256sum formats', () => {
  const mod = loadInstallModule();
  const bare = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855';
  const paired = bare + '  mssql-mcp-0.1.0-linux-x64.tar.gz';
  const star = bare + ' *mssql-mcp-0.1.0-linux-x64.tar.gz';
  assert.strictEqual(mod.parseChecksumFile(bare), bare);
  assert.strictEqual(mod.parseChecksumFile(paired), bare);
  assert.strictEqual(mod.parseChecksumFile(star), bare);
});

check('classifyDownloadError classifies HTTP 404 as release not published', () => {
  const mod = loadInstallModule();
  const err = new Error('HTTP 404 for https://github.com/.../mssql-mcp-0.1.0-linux-x64.tar.gz');
  assert.strictEqual(mod.classifyDownloadError(err), 'release not published yet');
});

check('classifyDownloadError classifies ECONNRESET as network or proxy blocked', () => {
  const mod = loadInstallModule();
  const err = new Error('connect ECONNRESET');
  err.code = 'ECONNRESET';
  assert.strictEqual(mod.classifyDownloadError(err), 'network or proxy blocked');
});

check('classifyDownloadError classifies ENOTFOUND as network or proxy blocked', () => {
  const mod = loadInstallModule();
  const err = new Error('getaddrinfo ENOTFOUND github.com');
  err.code = 'ENOTFOUND';
  assert.strictEqual(mod.classifyDownloadError(err), 'network or proxy blocked');
});

check('classifyDownloadError classifies ECONNREFUSED as network or proxy blocked', () => {
  const mod = loadInstallModule();
  const err = new Error('connect ECONNREFUSED 127.0.0.1:443');
  err.code = 'ECONNREFUSED';
  assert.strictEqual(mod.classifyDownloadError(err), 'network or proxy blocked');
});

check('classifyDownloadError classifies timeout as timed out', () => {
  const mod = loadInstallModule();
  const err = new Error('timeout downloading https://github.com/.../archive');
  assert.strictEqual(mod.classifyDownloadError(err), 'timed out');
});

check('classifyDownloadError falls back to download failed for unknown errors', () => {
  const mod = loadInstallModule();
  assert.strictEqual(mod.classifyDownloadError(new Error('something weird')), 'download failed');
  assert.strictEqual(mod.classifyDownloadError(new Error('HTTP 500 for ...')), 'download failed');
});

if (failures > 0) {
  console.error('\n' + failures + ' test(s) failed.');
  process.exit(1);
} else {
  console.log('\nAll smoke tests passed.');
}
