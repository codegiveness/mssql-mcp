# Binary delivery via optionalDependencies + shim self-heal

Replace the `postinstall`-based binary download with platform-specific `optionalDependencies` that bundle the prebuilt binary, plus a shim that self-heals from GitHub Releases when an optional dep is absent. Supersedes the "install.js contract (npm postinstall)" section of ADR-0014.

## Context

ADR-0014 established a postinstall contract: `npm install` runs `install.js`, which downloads the correct RID archive from GitHub Releases, verifies its SHA256, extracts it, and overwrites the Node shim at `bin/mssql-mcp` with the real native binary. The contract was deliberately strict — fail loudly on any error, no silent fallback, no retry.

This contract broke in practice. `npx -y @codegiveness/mssql-mcp` failed with "binary not installed" because postinstall did not run (or ran and failed silently). Common causes: npm's `--ignore-scripts` flag, corporate npm configs setting `ignore-scripts=true`, npx cache quirks that skip lifecycle scripts, and proxy/firewall blocks on the GitHub Releases download. The shim at `bin/mssql-mcp` detected the absence and printed a dead-end error with no recovery path.

The reference benchmark is `npx -y @colbymchenry/codegraph`, which works reliably on a clean machine with only Node installed. Codegraph achieves this via: (1) a thin main npm package, (2) platform-specific `optionalDependencies` each bundling the native binary, (3) a shim that resolves the optional dep and self-heals from GitHub Releases into `~/.codegraph/bundles/` when the optional dep is absent. No postinstall involvement at all.

The new constraint, confirmed in grilling: the user has Node + `npx` but nothing else (no .NET SDK, no global installs), and the install must NOT depend on postinstall running.

## Decision

### 1. Binary delivery via `optionalDependencies` (no postinstall)

Publish 5 per-platform npm packages, one per RID, each containing the prebuilt binary at the package root:

| Package | RID | Binary type |
|---|---|---|
| `@codegiveness/mssql-mcp-linux-x64` | `linux-x64` | self-contained |
| `@codegiveness/mssql-mcp-linux-arm64` | `linux-arm64` | self-contained |
| `@codegiveness/mssql-mcp-osx-x64` | `osx-x64` | self-contained |
| `@codegiveness/mssql-mcp-osx-arm64` | `osx-arm64` | self-contained |
| `@codegiveness/mssql-mcp-win-x64` | `win-x64` | framework-dependent (.NET 10 runtime required) |

Each per-platform package declares `os` and `cpu` fields so npm installs only the matching one:

```json
{
  "name": "@codegiveness/mssql-mcp-linux-x64",
  "version": "0.3.0",
  "os": ["linux"],
  "cpu": ["x64"],
  "files": ["mssql-mcp"]
}
```

The main package `@codegiveness/mssql-mcp` declares all 5 as `optionalDependencies` and has **no `postinstall` script** and **no `install.js`**:

```json
{
  "name": "@codegiveness/mssql-mcp",
  "version": "0.3.0",
  "bin": { "mssql-mcp": "bin/mssql-mcp.js" },
  "optionalDependencies": {
    "@codegiveness/mssql-mcp-linux-x64": "0.3.0",
    "@codegiveness/mssql-mcp-linux-arm64": "0.3.0",
    "@codegiveness/mssql-mcp-osx-x64": "0.3.0",
    "@codegiveness/mssql-mcp-osx-arm64": "0.3.0",
    "@codegiveness/mssql-mcp-win-x64": "0.3.0"
  },
  "scripts": {},
  "files": ["bin/mssql-mcp.js"]
}
```

npm installs the correct per-platform package as part of the normal dependency resolution — no lifecycle script involved. This works even when `--ignore-scripts` is set.

### 2. Two-tier shim with runtime self-heal

The `bin/mssql-mcp.js` shim (replacing the old `bin/mssql-mcp` Node shim) runs on every invocation. It resolves the binary in this order:

1. **Happy path — optional dep present.** Resolve `@codegiveness/mssql-mcp-<rid>/mssql-mcp` via `require.resolve`. If found, exec it.
2. **Self-heal path — optional dep absent.** Download the matching RID archive from GitHub Releases into `~/.mssql-mcp/bin/<version>/<rid>/`, verify SHA256 against the sidecar, extract, `chmod 755` (Unix), and exec. Subsequent invocations hit the cache — no network.

The self-heal path is the safety net for environments where optionalDeps are stripped (`--no-optional`, corporate npm mirrors). It is not the primary mechanism — the optionalDep is.

### 3. Cache location

Downloaded binaries cache at `~/.mssql-mcp/bin/<version>/<rid>/mssql-mcp` (Unix) or `mssql-mcp.exe` (Windows). Matches the codegraph precedent (`~/.codegraph/bundles/`). Survives reboots, predictable for users who need to inspect or clear it.

### 4. Self-heal controls

- **`MSSQL_MCP_NO_DOWNLOAD=1`** — disables the self-heal download entirely. When set and the optional dep is absent, the shim prints the manual download URL and `dotnet tool install` fallback, then exits non-zero. For locked-down environments where outbound network is blocked.
- **SHA256 verification** — the self-heal path verifies the archive hash against the `.sha256` sidecar, reusing the existing `parseChecksumFile` + `sha256Hex` logic from `install.js`.

### 5. Error message contract

Every failure mode prints: (1) the problem and RID, (2) the direct GitHub Releases URL for manual download, (3) the `dotnet tool install -g mssql-mcp` fallback. No dead ends. Three failure modes:

- **Unsupported platform** — no RID match. Print supported RIDs and the `dotnet tool` fallback.
- **Windows .NET 10 missing** — framework-dependent binary can't start. Print the runtime download URL and the `dotnet tool` fallback.
- **Self-heal download failed or disabled** — network error, 404, checksum mismatch, or `MSSQL_MCP_NO_DOWNLOAD=1`. Print the manual download URL and the `dotnet tool` fallback.

### 6. `install.js` deleted; `mssql-mcp-cli` deprecated

- `npm/install.js` is deleted. Its RID mapping, download, checksum, and extract logic moves into `bin/mssql-mcp.js` (the shim), augmented with the cache + self-heal path.
- The `postinstall` script is removed from `package.json`.
- `mssql-mcp-cli` (the old unscoped package) is deprecated on npm, not republished. Existing v0.2.0 stays installable for backward compatibility; the npm deprecation notice directs users to `@codegiveness/mssql-mcp`.

### 7. Release pipeline: single sequential publish job, main last

The existing build matrix produces 5 RID archives + checksums (unchanged). A new publish job (after the matrix) downloads all 5 archives, extracts each into a per-platform package directory, writes the per-platform `package.json` with matching `name`/`os`/`cpu`/`version`, and runs `npm publish` for each of the 5 per-platform packages, then publishes the main package last. Sequential, fail-fast. The main package publishes only after all 5 per-platform packages succeed, so users never see a main package whose optionalDeps can't resolve.

### 8. Windows: framework-dependent, honest failure

Windows (`win-x64`) ships a framework-dependent binary (per ADR-0002 — `Microsoft.Data.SqlClient.SNI`'s "Distributable Code" license conservatively blocks self-contained redistribution under MIT). The binary is bundled via the `win-x64` optional dep, so it lands on disk postinstall-free. But it requires the .NET 10 runtime on the host. The shim detects a missing runtime at startup and prints the runtime download URL + `dotnet tool install` fallback. Windows is not one-shot until the SNI license issue is resolved — that investigation is a separate follow-up ADR.

## Considered Options

- **A. Bundle via optionalDependencies + shim self-heal ✅** — chosen. Industry-standard pattern (esbuild, playwright, codegraph). Eliminates the entire class of postinstall-didn't-run failures. Self-heal is a safety net for the minority case where optionalDeps are stripped. Cost: 6 npm packages per release instead of 1.
- **B. Lazy runtime download only (no optionalDeps)** — rejected. Trades one flaky moment (install) for another (first run). A stdio MCP server can't show download progress. Write-access to cache dir can fail in containers. No resilience gain over postinstall.
- **C. Keep postinstall, improve the error message** — rejected. Doesn't solve the `--ignore-scripts` case; just makes the failure prettier. Still fundamentally broken when GitHub Releases is unreachable at install time.
- **D. Bundle via optionalDependencies, no self-heal (single-tier)** — rejected. Not truly one-shot when optionalDeps are stripped. The self-heal path costs bounded complexity (existing install.js logic relocated) for high resilience.

## Consequences

- **`npx -y @codegiveness/mssql-mcp` works postinstall-free.** The binary arrives via npm's dependency resolution, not a lifecycle script. This is the core fix.
- **6 npm packages per release** instead of 1. The release pipeline gains a publish job that handles all 6 sequentially. Maintenance cost: one publish script, not a second codebase — the per-platform packages are just `package.json` + binary.
- **`install.js` is deleted.** Its logic is relocated into the shim. The smoke test (`npm/test.js`) is updated to test the shim's RID mapping and checksum logic instead of `install.js` exports.
- **`mssql-mcp-cli` is frozen at v0.2.0 and deprecated.** Existing users keep working; migration is opt-in via the npm deprecation notice.
- **ADR-0014's postinstall contract is superseded.** The "fail loudly, no fallback, no retry" contract is replaced by: optionalDep for the happy path, self-heal download with cache for the unhappy path, fail loudly only when both fail. The rest of ADR-0014 (tag-triggered release, CI on main, dual stability contract, graduation triggers) is untouched.
- **Windows is not one-shot.** The .NET 10 runtime dependency remains. The shim makes the failure honest and actionable, but Windows users without .NET must install it. Path 2 (resolve the SNI license for self-contained Windows) is a separate follow-up ADR.
- **Self-heal download can fail at runtime.** First run on a stripped-optionalDep environment does a network download. If that fails (air-gapped, proxy), the error message provides the manual download URL. `MSSQL_MCP_NO_DOWNLOAD=1` lets locked-down environments skip the attempt and fail immediately with guidance.
- **Cache at `~/.mssql-mcp/bin/`** is a new user-visible directory. Documented in README's Installation section. Users can clear it; the shim re-downloads on next run.
