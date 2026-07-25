# Security Research Result — Hardening Batch

## Verdict

**PASS** — 7 confirmed findings fixed, 437 unit tests pass, all 10 pre-push checks pass.

## Scope

- **Target:** mssql-mcp `security-hardening` branch
- **Base/diff:** hardening work across 7 focused fixes on the npm shim (`npm/bin/mssql-mcp.js`) and core server (`src/mssql-mcp.Core/**`, `src/mssql-mcp.Tools/**`)
- **Full-repo audit surfaces audited:** npm shim archive extraction, download/cache logic, C# Guard, tool authorization, logging, password obfuscation, SQL helper validation
- **Commands run:** `dotnet test --filter Category!=Integration` (437 passed, 0 failed, 4 skipped integration), `dotnet format mssql-mcp.sln --verify-no-changes --no-restore`, `node npm/test.js`, `node npm/poc-tests.js`, `./scripts/mcp-smoke.sh`, `dotnet run --project src/mssql-mcp -- --validate`, `dotnet run --project src/mssql-mcp -- --help`, `dotnet run --project src/mssql-mcp -- upgrade`, `node scripts/lint-readme-snippets.js`, `node scripts/check-version-consistency.js`

## Team

| Member | Role | Category |
|--------|------|----------|
| surface-hunter | Attack surface mapping | deep |
| auth-data-hunter | Auth/injection/credential hunting | ultrabrain |
| runtime-supply-hunter | Filesystem/subprocess/supply-chain hunting | unspecified-high |
| poc-engineer-a | PoC construction + falsification | unspecified-high |
| poc-engineer-b | Independent PoC reproduction | deep |

## Findings

| Severity | Title | CWE | Exploitability | Impact | PoC | Fix |
|----------|-------|-----|----------------|--------|-----|-----|
| Medium | extractTarGz path/symlink traversal (F1) | CWE-22, CWE-59, CWE-61 | Crafted archive with `..`, absolute paths, or symlink/hardlink entries writes outside target dir | Arbitrary file write / binary substitution during self-heal | Confirmed via `npm/poc-tests.js` PB3 (inverted) | Fixed: two-phase tar validation rejects traversal and link entries before extraction |
| Medium | Cross-DB authz gap (F2) | CWE-639, CWE-732 | Agent passes `database` param for a DB the login cannot access; discovery tools still query it | Unauthorized cross-database read | Confirmed via `tests/mssql-mcp.Tools.Tests/PocCrossDbAuthzTests.cs` CB1-CB4 (inverted) | Fixed: `ValidateDatabaseSql` requires `HAS_DBACCESS(name) = 1`; affects 6 tools |
| Low-Medium | FileLoggerProvider path traversal (F3) | CWE-22, CWE-59, CWE-61 | `MSSQL_LOG_FILE` env var contains `..` or points at a symlink | Log file written outside intended directory | Confirmed via `tests/mssql-mcp.Core.Tests/PocLeakTests3.cs` PB2 (inverted) | Fixed: `ValidatePath` rejects `..` and symlinks in constructor and `CreateWriter` |
| Medium | npm cache poisoning (F5) | CWE-345, CWE-494 | Local cache binary is replaced; shim trusts cache hit without re-verification | Malicious binary execution on next run | Confirmed via `npm/poc-tests.js` PB5 (4 tests) | Fixed: `resolveCachedBinary` re-verifies sha256 sidecar on every cache hit; mismatch purges cache |
| Low-Medium | PasswordObfuscator regex partial leak (F6) | CWE-209, CWE-532 | Pathological password value causes regex to match a prefix, leaving remainder unobfuscated | Credential fragment in log/error output | Confirmed via `tests/mssql-mcp.Core.Tests/PocObfuscationPartialLeakTests.cs` PA2-PA6 (inverted) | Fixed: regex consumes full value after delimiter; timeout returns redaction string |
| Low-Medium | fetchUrl redirect host pinning (F7) | CWE-601 | GitHub redirect points to attacker-controlled host | Malicious binary download | Confirmed via `npm/poc-tests.js` PB4 (inverted) | Fixed: redirect host allowlist pins to github.com and `*.githubusercontent.com` |

## Finding Details

### F1: extractTarGz path/symlink traversal (FIXED)

- **Evidence:** `npm/bin/mssql-mcp.js:extractTarGz` previously called `tar -xzf` directly, trusting the archive contents.
- **Attack path:** Attacker crafts a release archive containing entries such as `../mssql-mcp` (traversal), `/etc/passwd` (absolute), or symlink/hardlink entries. Extraction writes outside the target directory, potentially overwriting the downloaded binary or system files.
- **PoC:** `npm/poc-tests.js` PB3 — inverted to assert `extractTarGz` throws on a crafted traversal archive.
- **Fix:** Two-phase validation before extraction: `tar -tzf` rejects `..` and `/`-prefixed entries; `tar -tvf` rejects type flags `l` and `h`. Only regular files and directories are extracted.
- **Regression check:** PB3 contrast test plus inverted traversal test pass.

### F2: Cross-DB authorization gap (FIXED)

- **Evidence:** `src/mssql-mcp.Core/SqlHelpers.cs:ValidateDatabaseSql` checked `state = 0` (online) and `is_user_database = 1`, but not whether the login could access the database.
- **Attack path:** Agent calls `list_objects` or `analyze_indexes` with `database=OtherDb` where the login has no user mapping. The tool issues metadata queries against the other database and returns rows.
- **PoC:** `tests/mssql-mcp.Tools.Tests/PocCrossDbAuthzTests.cs` CB1-CB4 — inverted to assert validation fails for inaccessible DBs.
- **Fix:** Appended `AND HAS_DBACCESS(name) = 1` to the validation query. Inaccessible DBs return zero rows, surfacing the existing "does not exist" error.
- **Behavior change:** 6 tools (`list_schemas`, `list_objects`, `get_object_details`, `analyze_indexes`, `get_top_queries`, `analyze_db_health`) now reject inaccessible databases. This is intentional.
- **Regression check:** CB1-CB4 pass; `get_top_queries` included because it reads `sys.dm_exec_query_stats` in the context of the requested database.

### F3: FileLoggerProvider path traversal (FIXED)

- **Evidence:** `src/mssql-mcp.Core/Logging/FileLoggerProvider.cs` accepted `MSSQL_LOG_FILE` without traversal or symlink checks.
- **Attack path:** Operator (or compromised environment) sets `MSSQL_LOG_FILE=../outside/log.txt` or a symlink to `/etc/passwd`. Log writes escape the intended directory.
- **PoC:** `tests/mssql-mcp.Core.Tests/PocLeakTests3.cs` PB2 — inverted to assert `ArgumentException` on traversal paths.
- **Fix:** `ValidatePath` rejects raw `..` sequences, resolves to absolute path, and refuses symlinks whose target differs from the configured path.
- **Regression check:** PB2 tests assert no file is created for traversal paths and symlinks are rejected.

### F5: npm cache poisoning (FIXED)

- **Evidence:** `npm/bin/mssql-mcp.js:resolveCachedBinary` returned the cached binary path on cache hit without re-verifying integrity.
- **Attack path:** Local attacker replaces the cached binary. The shim runs the malicious binary on the next invocation.
- **PoC:** `npm/poc-tests.js` PB5 — 4 tests covering mismatched sidecar, missing sidecar, matching sidecar, and no cached binary.
- **Fix:** On cache hit, read `.sha256` sidecar, recompute binary sha256, compare. Mismatch deletes both files and falls back to download. Missing sidecar falls back to download without deleting the binary. `selfHealOrDie` writes the sidecar after extraction.
- **Migration note:** First run after the fix on a machine with an existing cache but no sidecar will re-download once. Subsequent runs use the cache normally.
- **Regression check:** PB5 x4 pass.

### F6: PasswordObfuscator regex partial leak (FIXED)

- **Evidence:** `src/mssql-mcp.Core/Logging/PasswordObfuscator.cs` regex alternation matched a shorter prefix of malformed password values.
- **Attack path:** Exception message contains `Password="x"y";tail=4;` or odd quote runs. Regex matched only `"x"`, leaving `y";tail=4;` unobfuscated in log or error output.
- **PoC:** `tests/mssql-mcp.Core.Tests/PocObfuscationPartialLeakTests.cs` PA2-PA6 — inverted to assert NO leak.
- **Fix:** Added `[^;]*` after quoted and braced branches to consume trailing characters before the next key. Timeout fallback now returns `[redacted: regex timeout]` instead of the raw input.
- **Regression check:** PA2-PA6 pass, including PA6 for semicolons inside quoted values.

### F7: fetchUrl redirect host pinning (FIXED)

- **Evidence:** `npm/bin/mssql-mcp.js:fetchUrl` followed up to 3 redirects without checking the final host.
- **Attack path:** A redirect chain ends at an attacker-controlled host serving a malicious archive and checksum.
- **PoC:** `npm/poc-tests.js` PB4 — inverted to assert redirects to untrusted hosts are rejected.
- **Fix:** Resolved redirect locations with `new URL(location, url)`, then checked the host against an allowlist (`github.com`, `objects.githubusercontent.com`, `github-releases.githubusercontent.com`, `*.githubusercontent.com`).
- **Regression check:** PB4 tests pass for allowlist acceptance and untrusted-host rejection.

## Downgraded or Rejected Candidates

| Candidate | Reason |
|-----------|--------|
| F4 (not in batch) | No separate F4 finding was tracked in this batch. |
| Metis gap analysis | No additional Metis gaps were identified beyond the 6 code findings. |

## Residual Risk

- **Cross-DB read via 3-part names in `execute_sql`:** Still documented behavior per the 2026-07-24 audit. `Restricted` mode enforces statement-type safety, not data-scope safety. Least-privilege SQL logins remain the mitigation.
- **PasswordObfuscator scope:** Still scrubs only `Password=`, `PWD=`, `AccessToken=`, and `Token=` patterns. Login names, server names, and other connection-string fragments may still appear in exception messages.
- **Unrestricted mode:** By design, allows any SQL including `xp_cmdshell`. Operators must not grant it to untrusted agents.
- **No live SQL Server testing:** All PoCs used mocked executors or Node test harnesses. Integration tests requiring a live SQL Server were not run for this batch.
