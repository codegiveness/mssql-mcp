// npm postinstall: download the prebuilt mssql-mcp binary for this platform,
// verify its SHA256 checksum, and replace the Node shim at bin/mssql-mcp with
// the real native executable.
//
// Contract (ADR-0014):
//   - Fail loudly on ANY error (no network, 404, checksum mismatch, extraction
//     failure, unsupported platform). Exit non-zero. No silent fallback to the
//     Node shim — the shim is NOT a real server.
//   - No retry logic in v1. The user retries manually.
//   - Detect unsupported platform/arch BEFORE download; print supported RIDs.
//   - Verify SHA256 after download; fail on mismatch.
//   - Print final binary path on success.
//
// Fall-back command suggested in every error message:
//   dotnet tool install -g mssql-mcp
'use strict';

const crypto = require('crypto');
const fs = require('fs');
const https = require('https');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');

const REPO = 'codegiveness/mssql-mcp';
const BIN_DIR = path.join(__dirname, 'bin');
const BIN_PATH = path.join(BIN_DIR, 'mssql-mcp');

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

function die(message) {
  console.error('mssql-mcp: ' + message);
  console.error('');
  console.error('Falling back is not automatic. You can install the .NET tool instead:');
  console.error('  dotnet tool install -g mssql-mcp');
  process.exit(1);
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

function sha256Hex(buf) {
  return crypto.createHash('sha256').update(buf).digest('hex');
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

function extractTarGz(archivePath, outDir) {
  const r = spawnSync('tar', ['-xzf', archivePath, '-C', outDir], { stdio: 'inherit' });
  if (r.status !== 0) {
    throw new Error('tar exited with status ' + r.status);
  }
}

function extractZip(archivePath, outDir) {
  // Prefer system unzip; Node has no built-in zip extractor before v22.
  const r = spawnSync('unzip', ['-o', archivePath, '-d', outDir], { stdio: 'inherit' });
  if (r.status !== 0) {
    throw new Error('unzip exited with status ' + r.status + ' (is unzip installed?)');
  }
}

async function main() {
  const rid = ridFor(process.platform, process.arch);
  if (!rid) {
    console.error('mssql-mcp: unsupported platform/architecture.');
    console.error('  detected: platform=' + process.platform + ', arch=' + process.arch);
    console.error('  supported RIDs: linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64');
    die('cannot resolve a binary for this platform.');
    return; // unreachable; die exits
  }

  // Resolve version from this package's package.json.
  const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, 'package.json'), 'utf8'));
  const version = pkg.version;
  if (!version) {
    die('package.json has no version field; cannot determine which release to download.');
    return;
  }
  const tag = 'v' + version;

  const ext = archiveExt(rid);
  const basename = 'mssql-mcp-' + version + '-' + rid + ext;
  const archiveUrl = 'https://github.com/' + REPO + '/releases/download/' + tag + '/' + basename;
  const checksumUrl = archiveUrl + '.sha256';

  console.log('mssql-mcp: installing v' + version + ' for ' + rid);
  console.log('mssql-mcp: downloading ' + archiveUrl);

  let archiveBuf;
  try {
    archiveBuf = await fetchUrl(archiveUrl);
  } catch (e) {
    die('download failed for archive: ' + e.message + '\n  URL: ' + archiveUrl);
    return;
  }

  console.log('mssql-mcp: downloading ' + checksumUrl);
  let checksumBuf;
  try {
    checksumBuf = await fetchUrl(checksumUrl);
  } catch (e) {
    die('download failed for checksum: ' + e.message + '\n  URL: ' + checksumUrl);
    return;
  }

  const expected = parseChecksumFile(checksumBuf.toString('utf8'));
  const actual = sha256Hex(archiveBuf);
  if (expected !== actual) {
    die(
      'SHA256 mismatch.\n' +
      '  expected: ' + expected + '\n' +
      '  actual:   ' + actual + '\n' +
      '  archive:  ' + archiveUrl
    );
    return;
  }
  console.log('mssql-mcp: checksum verified (' + expected + ')');

  // Stage the archive in a temp dir, extract, then move the binary into bin/.
  if (!fs.existsSync(BIN_DIR)) fs.mkdirSync(BIN_DIR, { recursive: true });

  const tmpRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'mssql-mcp-install-'));
  try {
    const archivePath = path.join(tmpRoot, basename);
    fs.writeFileSync(archivePath, archiveBuf);

    const extractDir = path.join(tmpRoot, 'extract');
    fs.mkdirSync(extractDir, { recursive: true });
    if (ext === '.zip') {
      extractZip(archivePath, extractDir);
    } else {
      extractTarGz(archivePath, extractDir);
    }

    // Flat archive per sqz: binary at archive root, named `mssql-mcp` (Unix) or
    // `mssql-mcp.exe` (Windows). Look for both to be defensive.
    const candidates = [
      path.join(extractDir, 'mssql-mcp'),
      path.join(extractDir, 'mssql-mcp.exe'),
    ].filter((p) => fs.existsSync(p));
    if (candidates.length === 0) {
      die('extraction produced no `mssql-mcp` binary. Archive: ' + archiveUrl);
      return;
    }
    const extractedBin = candidates[0];

    // Overwrite the shim with the real binary.
    fs.copyFileSync(extractedBin, BIN_PATH);
    if (process.platform !== 'win32') {
      fs.chmodSync(BIN_PATH, 0o755);
    }

    console.log('mssql-mcp: installed binary to ' + BIN_PATH);
  } finally {
    // Best-effort cleanup; never fail the install over tempdir removal.
    try {
      fs.rmSync(tmpRoot, { recursive: true, force: true });
    } catch (_) {
      // ignored
    }
  }
}

// Run main() only when invoked directly as `node install.js` (npm postinstall),
// not when required by the smoke test (npm/test.js).
if (require.main === module) {
  main().catch((e) => {
    die('unexpected error: ' + (e && e.stack ? e.stack : String(e)));
  });
}

// Exposed for the smoke test in npm/test.js. Not part of the public API.
module.exports = { ridFor, archiveExt, parseChecksumFile };
