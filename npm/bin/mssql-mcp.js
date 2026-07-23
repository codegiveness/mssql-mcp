#!/usr/bin/env node
'use strict';

// Shim: resolves the per-platform optionalDependency and execs the native binary.
// Postinstall-independent — npm's dependency resolution delivers the binary,
// not a lifecycle script. When the optional dep is absent (stripped by
// --no-optional, corporate npm mirrors), the shim self-heals by downloading
// from GitHub Releases into ~/.mssql-mcp/bin/<version>/<rid>/ (ADR-0028 §2).

const { spawnSync } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const https = require('https');
const os = require('os');
const path = require('path');

const REPO = 'codegiveness/mssql-mcp';
const NO_DOWNLOAD_ENV = 'MSSQL_MCP_NO_DOWNLOAD';
const CACHE_ROOT = path.join(os.homedir(), '.mssql-mcp', 'bin');

// (platform, arch) -> RID. win-x64 is framework-dependent per ADR-0002.
function ridFor(platform, arch) {
  if (platform === 'linux' && arch === 'x64') return 'linux-x64';
  if (platform === 'linux' && arch === 'arm64') return 'linux-arm64';
  if (platform === 'darwin' && arch === 'x64') return 'osx-x64';
  if (platform === 'darwin' && arch === 'arm64') return 'osx-arm64';
  if (platform === 'win32' && arch === 'x64') return 'win-x64';
  return null;
}

function archiveExt(rid) {
  return rid === 'win-x64' ? '.zip' : '.tar.gz';
}

function sha256Hex(buf) {
  return crypto.createHash('sha256').update(buf).digest('hex');
}

// Classify a download error into an actionable reason string.
// Inspects the error message for HTTP status codes and the error code for
// network-level failures (ECONNRESET, ENOTFOUND, ECONNREFUSED).
function classifyDownloadError(err) {
  if (err && err.message && /HTTP 404\b/.test(err.message)) return 'release not published yet';
  if (err && (err.code === 'ECONNRESET' || err.code === 'ENOTFOUND' || err.code === 'ECONNREFUSED')) {
    return 'network or proxy blocked';
  }
  if (err && err.message && /timeout/i.test(err.message)) return 'timed out';
  return 'download failed';
}

// Parse a .sha256 sidecar file. Accepts "<hex>  <filename>" or bare "<hex>".
function parseChecksumFile(text) {
  const trimmed = text.trim();
  if (/^[0-9a-fA-F]{64}$/.test(trimmed)) return trimmed.toLowerCase();
  // "<hex>  <filename>" form (sha256sum output).
  const match = trimmed.match(/^([0-9a-fA-F]{64})\s+\*?\S/);
  if (match) return match[1].toLowerCase();
  // Last resort: first 64-hex-char run anywhere in the file.
  const any = trimmed.match(/[0-9a-fA-F]{64}/);
  if (any) return any[0].toLowerCase();
  throw new Error('checksum file has no 64-hex SHA256 token');
}

// Follow up to 3 HTTP redirects (GitHub releases redirect to S3/CDN).
// Hard depth limit prevents redirect loops from hanging or stack-overflowing.
const MAX_REDIRECTS = 3;

function fetchUrl(url, depth) {
  if (depth === undefined) depth = 0;
  return new Promise((resolve, reject) => {
    if (depth > MAX_REDIRECTS) {
      reject(new Error('too many redirects (>' + MAX_REDIRECTS + ') starting from ' + url));
      return;
    }
    const req = https.get(url, (res) => {
      if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
        res.resume();
        fetchUrl(res.headers.location, depth + 1).then(resolve, reject);
        return;
      }
      if (res.statusCode !== 200) {
        res.resume();
        reject(new Error('HTTP ' + res.statusCode + ' for ' + url));
        return;
      }
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => resolve(Buffer.concat(chunks)));
    });
    req.on('error', reject);
    req.setTimeout(60000, () => {
      req.destroy(new Error('timeout downloading ' + url));
    });
  });
}

function extractTarGz(archivePath, outDir) {
  const r = spawnSync('tar', ['-xzf', archivePath, '-C', outDir], { stdio: 'inherit' });
  if (r.status !== 0) {
    throw new Error('tar exited with status ' + r.status);
  }
}

function extractZip(archivePath, outDir) {
  // Validate entry names before extraction to prevent path traversal (Zip Slip).
  // `unzip -o` alone does not reject entries with ../ or absolute paths.
  const list = spawnSync('unzip', ['-l', archivePath], { encoding: 'utf8' });
  if (list.status !== 0) {
    throw new Error('unzip -l failed with status ' + list.status);
  }
  const resolvedOut = path.resolve(outDir);
  for (const line of list.stdout.split('\n')) {
    const m = line.match(/^\s+\d+\s+\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}\s+(.+)$/);
    if (!m) continue;
    const entryPath = m[1].trim();
    const resolvedEntry = path.resolve(outDir, entryPath);
    if (!resolvedEntry.startsWith(resolvedOut + path.sep) && resolvedEntry !== resolvedOut) {
      throw new Error('Refusing to extract zip entry outside target directory: ' + entryPath);
    }
  }

  const r = spawnSync('unzip', ['-o', archivePath, '-d', outDir], { stdio: 'inherit' });
  if (r.status !== 0) {
    throw new Error('unzip exited with status ' + r.status + ' (is unzip installed?)');
  }
}

// --- Entry point ---

async function main() {
  const rid = ridFor(process.platform, process.arch);
  if (!rid) {
    failUnsupportedPlatform();
    return; // unreachable; failUnsupportedPlatform exits
  }

  // Happy path: resolve the per-platform optional dependency.
  const pkgName = '@codegiveness/mssql-mcp-' + rid;
  const binaryName = process.platform === 'win32' ? 'mssql-mcp.exe' : 'mssql-mcp';

  let binaryPath = null;
  try {
    binaryPath = require.resolve(pkgName + '/' + binaryName);
  } catch (_) {
    // Optional dep absent — fall through to self-heal path.
  }

  if (!binaryPath) {
    binaryPath = resolveCachedBinary(rid);
    if (!binaryPath) {
      // No cached binary — attempt self-heal download or fail with guidance.
      binaryPath = await selfHealOrDie(rid);
      if (!binaryPath) return; // unreachable; selfHealOrDie exits on failure
    }
  }

  // Exec the binary, forwarding stdio.
  const result = spawnSync(binaryPath, process.argv.slice(2), { stdio: 'inherit' });

  // Windows framework-dependent binary without .NET 10 runtime fails to spawn.
  // Detect and provide actionable guidance.
  if (result.error && process.platform === 'win32') {
    failWindowsDotNetMissing(result.error);
    return; // unreachable
  }

  if (result.error) {
    console.error('mssql-mcp: failed to launch binary: ' + result.error.message);
    process.exit(1);
  }
  process.exit(result.status ?? 1);
}

// Check if a cached binary exists at ~/.mssql-mcp/bin/<version>/<rid>/mssql-mcp[.exe]
function resolveCachedBinary(rid) {
  const pkg = require('../package.json');
  const version = pkg.version;
  const binaryName = process.platform === 'win32' ? 'mssql-mcp.exe' : 'mssql-mcp';
  const cachedPath = path.join(CACHE_ROOT, version, rid, binaryName);
  if (fs.existsSync(cachedPath)) {
    return cachedPath;
  }
  return null;
}

// Attempt self-heal download. Returns the binary path on success, or exits
// with an actionable error message on failure.
async function selfHealOrDie(rid) {
  const pkg = require('../package.json');
  const version = pkg.version;
  const tag = 'v' + version;
  const ext = archiveExt(rid);
  // Release archives are named with the tag (including the 'v' prefix):
  // mssql-mcp-v0.1.0-linux-x64.tar.gz — see release.yml Archive steps.
  const archiveName = 'mssql-mcp-' + tag + '-' + rid + ext;
  const archiveUrl = 'https://github.com/' + REPO + '/releases/download/' + tag + '/' + archiveName;
  const checksumUrl = archiveUrl + '.sha256';

  const noDownload = process.env[NO_DOWNLOAD_ENV] === '1';
  if (noDownload) {
    failDownloadDisabled(rid, archiveUrl);
    return null; // unreachable
  }

  const cacheDir = path.join(CACHE_ROOT, version, rid);
  const binaryName = process.platform === 'win32' ? 'mssql-mcp.exe' : 'mssql-mcp';
  const cachedBinaryPath = path.join(cacheDir, binaryName);

  console.error('mssql-mcp: native binary not found via npm; attempting self-heal download.');
  console.error('mssql-mcp: downloading ' + archiveUrl);

  let archiveBuf;
  try {
    archiveBuf = await fetchUrl(archiveUrl);
  } catch (e) {
    failDownloadError(rid, 'archive', classifyDownloadError(e), e.message, archiveUrl);
    return null; // unreachable
  }

  let checksumBuf;
  try {
    checksumBuf = await fetchUrl(checksumUrl);
  } catch (e) {
    failDownloadError(rid, 'checksum', classifyDownloadError(e), e.message, checksumUrl);
    return null; // unreachable
  }

  const expected = parseChecksumFile(checksumBuf.toString('utf8'));
  const actual = sha256Hex(archiveBuf);
  if (expected !== actual) {
    failChecksumMismatch(rid, expected, actual, archiveUrl);
    return null; // unreachable
  }

  // Extract into cache dir.
  fs.mkdirSync(cacheDir, { recursive: true });
  const tmpRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'mssql-mcp-heal-'));
  try {
    const archivePath = path.join(tmpRoot, archiveName);
    fs.writeFileSync(archivePath, archiveBuf);

    const extractDir = path.join(tmpRoot, 'extract');
    fs.mkdirSync(extractDir, { recursive: true });
    if (ext === '.zip') {
      extractZip(archivePath, extractDir);
    } else {
      extractTarGz(archivePath, extractDir);
    }

    // Flat archive: binary at archive root, named `mssql-mcp` (Unix) or
    // `mssql-mcp.exe` (Windows). Look for both to be defensive.
    const candidates = [
      path.join(extractDir, 'mssql-mcp'),
      path.join(extractDir, 'mssql-mcp.exe'),
    ].filter((p) => fs.existsSync(p));
    if (candidates.length === 0) {
      failExtractionFailed(rid, archiveUrl);
      return null; // unreachable
    }

    fs.copyFileSync(candidates[0], cachedBinaryPath);
    if (process.platform !== 'win32') {
      fs.chmodSync(cachedBinaryPath, 0o755);
    }

    console.error('mssql-mcp: cached binary to ' + cachedBinaryPath);
  } finally {
    try {
      fs.rmSync(tmpRoot, { recursive: true, force: true });
    } catch (_) {
      // ignored
    }
  }

  return cachedBinaryPath;
}

// --- Error message contract ---
// Every failure mode prints: (1) the problem and RID, (2) the direct GitHub
// Releases URL for manual download, (3) the `dotnet tool install -g codegiveness.mssql-mcp`
// fallback. No dead ends. See ADR-0028 §5.

function failUnsupportedPlatform() {
  console.error('mssql-mcp: unsupported platform/architecture.');
  console.error('  detected: platform=' + process.platform + ', arch=' + process.arch);
  console.error('  supported RIDs: linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64');
  console.error('');
  console.error('Install the .NET tool instead:');
  console.error('  dotnet tool install -g codegiveness.mssql-mcp');
  process.exit(1);
}

function failDownloadDisabled(rid, archiveUrl) {
  console.error('mssql-mcp: native binary not found and download is disabled');
  console.error('  (MSSQL_MCP_NO_DOWNLOAD=1 is set).');
  console.error('  RID: ' + rid);
  console.error('');
  console.error('Manual download:');
  console.error('  ' + archiveUrl);
  console.error('');
  console.error('Or install the .NET tool:');
  console.error('  dotnet tool install -g codegiveness.mssql-mcp');
  process.exit(1);
}

function failDownloadError(rid, kind, reason, message, url) {
  console.error('mssql-mcp: failed to download ' + kind + ' [' + reason + '].');
  console.error('  RID: ' + rid);
  console.error('  error: ' + message);
  console.error('  URL: ' + url);
  console.error('');
  console.error('Manual download:');
  console.error('  ' + url);
  console.error('');
  console.error('Or install the .NET tool:');
  console.error('  dotnet tool install -g codegiveness.mssql-mcp');
  process.exit(1);
}

function failChecksumMismatch(rid, expected, actual, archiveUrl) {
  console.error('mssql-mcp: SHA256 verification failed for downloaded archive.');
  console.error('  RID: ' + rid);
  console.error('  expected: ' + expected);
  console.error('  actual:   ' + actual);
  console.error('  archive:  ' + archiveUrl);
  console.error('');
  console.error('Manual download (re-verify the checksum yourself):');
  console.error('  ' + archiveUrl);
  console.error('');
  console.error('Or install the .NET tool:');
  console.error('  dotnet tool install -g codegiveness.mssql-mcp');
  process.exit(1);
}

function failExtractionFailed(rid, archiveUrl) {
  console.error('mssql-mcp: download succeeded but extraction produced no binary.');
  console.error('  RID: ' + rid);
  console.error('  archive: ' + archiveUrl);
  console.error('');
  console.error('Manual download:');
  console.error('  ' + archiveUrl);
  console.error('');
  console.error('Or install the .NET tool:');
  console.error('  dotnet tool install -g codegiveness.mssql-mcp');
  process.exit(1);
}

function failWindowsDotNetMissing(spawnError) {
  console.error('mssql-mcp: failed to launch the Windows binary.');
  console.error('  The Windows build is framework-dependent and requires the .NET 10 runtime.');
  console.error('  error: ' + spawnError.message);
  console.error('');
  console.error('Download the .NET 10 runtime:');
  console.error('  https://dotnet.microsoft.com/download');
  console.error('');
  console.error('Or install the .NET tool (includes the runtime):');
  console.error('  dotnet tool install -g codegiveness.mssql-mcp');
  process.exit(1);
}

// Exposed for smoke tests (npm/test.js). Not part of the public API.
module.exports = { ridFor, archiveExt, parseChecksumFile, classifyDownloadError, sha256Hex };

// Run main() only when invoked directly (not when required by test.js).
if (require.main === module) {
  main().catch((e) => {
    console.error('mssql-mcp: unexpected error: ' + (e && e.stack ? e.stack : String(e)));
    process.exit(1);
  });
}
