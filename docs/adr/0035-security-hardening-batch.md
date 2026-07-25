# Security hardening batch: 7 findings resolved

## Context

A focused security-hardening sprint on the `security-hardening` branch found and fixed 7 exploitable weaknesses in the mssql-mcp server and its npm distribution shim. The work was driven by inverted PoC tests: each test first proved the bug existed, then was rewritten to prove the fix under the same trigger conditions. All fixes were verified by the repo's 10-item pre-push checklist before this ADR was written.

The 7 findings span three surfaces:

- **npm shim (`npm/bin/mssql-mcp.js`)**: archive extraction, download redirect pinning, and cache integrity.
- **Core server (`src/mssql-mcp.Core/**`)**: log-file path traversal, password obfuscation, and cross-database authorization.
- **Tool layer (`src/mssql-mcp.Tools/**`)**: the `database` parameter validation used by 6 discovery tools.

Each fix is minimal, localized, and backed by a regression PoC test. No behavior changes were made beyond what the security findings required.

## Decision

1. **Finding 1 — extractTarGz path/symlink traversal (npm shim)**
   `extractTarGz` now validates every archive entry before extraction. It runs `tar -tzf` to reject entries containing `..` or starting with `/`, then `tar -tvf` to reject symlink (`l`) and hardlink (`h`) type flags. Only regular files and directories pass through to `tar -xzf`. PoC: `npm/poc-tests.js` PB3.

2. **Finding 2 — cross-DB authorization gap (`HAS_DBACCESS`)**
   `SqlHelpers.ValidateDatabaseSql` now appends `AND HAS_DBACCESS(name) = 1` to the database existence query. Databases the SQL login cannot access return zero rows and surface the existing "does not exist" error. This is a deliberate behavior change: 6 tools (`list_schemas`, `list_objects`, `get_object_details`, `analyze_indexes`, `get_top_queries`, `analyze_db_health`) now reject inaccessible databases through the two shared wrappers (`DatabaseTools.TryValidateDatabaseAsync` and `OpsTools.TryValidateDatabaseAsync`). `get_top_queries` is intentionally included because it reads `sys.dm_exec_query_stats` in the context of the requested database. PoC: `tests/mssql-mcp.Tools.Tests/PocCrossDbAuthzTests.cs` CB1-CB4.

3. **Finding 3 — FileLoggerProvider path traversal**
   `FileLoggerProvider.ValidatePath` rejects paths containing `..` on the raw string, resolves the path with `Path.GetFullPath`, and refuses existing symlinks whose link target differs from the configured path. The check is called from the constructor and from `CreateWriter`. Absolute paths remain permitted; only traversal and symlink escapes are blocked. PoC: `tests/mssql-mcp.Core.Tests/PocLeakTests3.cs` PB2.

4. **Finding 5 — npm cache poisoning (sha256 re-verify)**
   `resolveCachedBinary` reads the `.sha256` sidecar on every cache hit, recomputes the binary's sha256, and compares. A mismatch deletes both files and treats it as a cache miss. A missing sidecar also treats it as a cache miss, but leaves the binary in place (no tampering proven). `selfHealOrDie` writes the sidecar after extraction, chmod, and final placement. PoC: `npm/poc-tests.js` PB5.

5. **Finding 6 — PasswordObfuscator regex partial leak**
   The generated regex now consumes the full value after the closing delimiter using `[^;]*`, so malformed inputs like `Password="x"y";tail=4;` no longer leak the trailing `y";tail=4;`. On `RegexMatchTimeoutException` the obfuscator returns `[redacted: regex timeout]` instead of the raw input. PoC: `tests/mssql-mcp.Core.Tests/PocObfuscationPartialLeakTests.cs` PA2-PA6.

6. **Finding 7 — fetchUrl redirect host pinning**
   `fetchUrl` resolves redirect locations with `new URL(location, url)`, then checks the next host against an allowlist before recursing. The allowlist is `github.com`, `objects.githubusercontent.com`, `github-releases.githubusercontent.com`, and any host ending with `.githubusercontent.com`. Any other host aborts with `Refusing to follow redirect to untrusted host: <host>`. The existing `MAX_REDIRECTS=3` depth limit is unchanged. PoC: `npm/poc-tests.js` PB4.

7. **Metis gap analysis — none applicable**
   No additional Metis gap was identified during this batch. All findings came from direct source review and PoC construction.

## Consequences

- **Six discovery tools now reject databases the SQL login cannot access.** Operators using a least-privilege login will see no change. Operators who granted broader visibility will see `list_schemas`, `list_objects`, `get_object_details`, `analyze_indexes`, `get_top_queries`, and `analyze_db_health` return `Database '<name>' does not exist.` for inaccessible databases. This is intentional and closes the cross-DB read path.

- **First npm run after this fix may re-download on cache hit.** If a pre-existing cache contains the binary but no `.sha256` sidecar, `resolveCachedBinary` treats it as a miss and re-downloads once. The re-downloaded archive is sha256-verified and the sidecar is written, so subsequent runs use the cache normally.

- **Obfuscation now covers pathological inputs.** Odd quote runs, malformed braces, and semicolons inside quoted password values are fully redacted. Regex timeout is a defense-in-depth failure mode that still does not leak credentials.

- **Archive extraction is hardened against Tar Slip and symlink attacks.** The npm shim will not extract entries that traverse above the target directory or that are links.

- **Redirect following is pinned to GitHub infrastructure.** The shim remains safe even if GitHub's redirect chain changes, provided the new host is still within the GitHub CDN allowlist.

## PoC test links

| Finding | PoC test location |
|---|---|
| F1 extractTarGz traversal/symlink | `npm/poc-tests.js` PB3 |
| F2 cross-DB authz (`HAS_DBACCESS`) | `tests/mssql-mcp.Tools.Tests/PocCrossDbAuthzTests.cs` CB1-CB4 |
| F3 FileLoggerProvider path traversal | `tests/mssql-mcp.Core.Tests/PocLeakTests3.cs` PB2 |
| F5 npm cache poisoning | `npm/poc-tests.js` PB5 |
| F6 PasswordObfuscator partial leak | `tests/mssql-mcp.Core.Tests/PocObfuscationPartialLeakTests.cs` PA2-PA6 |
| F7 fetchUrl redirect pinning | `npm/poc-tests.js` PB4 |
