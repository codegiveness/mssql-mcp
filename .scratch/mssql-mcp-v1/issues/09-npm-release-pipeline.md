# 09 — npm distribution + release pipeline

**What to build:** `npx -y mssql-mcp` installs and runs the server on Linux x64/arm64 and macOS x64/arm64 (self-contained). Windows falls back to framework-dependent (SNI license blocks self-contained Windows per ADR-0002). The npm package uses the sqz pattern: Node shim in `npm/bin/` for immediate `npx` support, `install.js` postinstall downloads a flat tarball from GitHub Releases per RID, verifies SHA256, and overwrites the shim with the real binary. `release.yml` GitHub Actions workflow: tag `v*.*.*` triggers build for 5 RIDs, creates flat archives + SHA256 checksums, publishes GitHub Release + NuGet + npm. Full README with quickstart, badges, tool table, auth guide, config table, trademark disclaimer.

**Blocked by:** 07 (Unrestricted mode — need the full feature set before packaging), 08a (Structured error handling — errors must be finalized before packaging)

**Status:** ready-for-agent

- [ ] `npm/package.json`: `name: "mssql-mcp"`, `bin: {"mssql-mcp": "bin/mssql-mcp"}`, `scripts: {postinstall: "node install.js"}`, `files: ["bin/", "install.js"]`
- [ ] `npm/bin/mssql-mcp`: Node.js shim (20-line `spawnSync` wrapper) so `npx`/`npm link` works immediately after install
- [ ] `npm/install.js`: maps `process.platform`+`process.arch` → RID → downloads flat tarball from GitHub Releases → verifies SHA256 → extracts → overwrites shim with real binary → chmod 755
- [ ] `install.js` fail-loudly contract: no network, 404, checksum mismatch, extraction failure, unsupported platform — all exit non-zero with clear error naming RID, URL tried, and `dotnet tool install mssql-mcp` fallback
- [ ] `install.js` detects unsupported platform BEFORE download; prints supported RIDs
- [ ] `install.js` verifies SHA256 after download; fails on mismatch
- [ ] `install.js` prints final binary path on success
- [ ] No silent fallback to Node shim (shim is not a real server)
- [ ] No retry logic in v1
- [ ] `release.yml` GitHub Actions workflow: tag `v*.*.*` or `workflow_dispatch`
- [ ] Release builds 5 RIDs: linux-x64, linux-arm64, osx-x64, osx-arm64 (self-contained, `PublishSingleFile=true`), win-x64 (framework-dependent)
- [ ] Flat archives: `.tar.gz` for Unix, `.zip` for Windows (binary at archive root per sqz contract)
- [ ] SHA256 checksum file per archive
- [ ] GitHub Release created with archives + checksums
- [ ] NuGet push: `dotnet pack` + `dotnet nuget push`
- [ ] npm publish: sync `npm/package.json` version to tag, `npm publish`
- [ ] Sequential stages (NuGet push before npm publish; if NuGet fails, npm doesn't run)
- [ ] `ci.yml`: build + test + pack (no publish) on push to main and PRs
- [ ] `README.md`: title + one-line description + badges (CI, NuGet, npm, .NET 10, MIT)
- [ ] README: "Why this exists" section (2-3 sentences referencing broken alternatives)
- [ ] README: Quick start (npm) — Claude Desktop config with `npx -y mssql-mcp`
- [ ] README: Quick start (dotnet tool) — `dotnet tool install -g mssql-mcp`
- [ ] README: Access modes section (Restricted default + Unrestricted opt-in)
- [ ] README: Tools table (9 tools with one-line descriptions)
- [ ] README: Authentication section (SQL password, Windows Integrated, AD Default with examples)
- [ ] README: Configuration table (all 8 env vars from ADR-0015)
- [ ] README: Installation details (platform matrix, what install.js does)
- [ ] README: Security section (read-only default, Guard layers, transaction rollback)
- [ ] README: Development section (clone, restore, test, integration tests)
- [ ] README: Trademarks & licensing (MIT, THIRD-PARTY-NOTICES reference, "Not affiliated with Microsoft Corporation", CAL/multiplexing note)
- [ ] README: Contributing link to CONTRIBUTING.md
- [ ] README: "Stability" note: "mssql-mcp is currently `0.x`. The tool surface is stable; CLI/env/return shapes may change between minor versions before `1.0.0`."
- [ ] `.gitignore` includes `npm/bin/mssql-mcp` (real binary downloaded postinstall, not committed)
