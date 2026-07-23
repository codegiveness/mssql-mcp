# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.2] - 2026-07-23

### Fixed

- **Disabled `PublishTrimmed` on Linux/macOS self-contained builds** to fix issue #44 (all MCP tools crash on the trimmed published binary). `PublishTrimmed=true` disables `System.Text.Json` reflection-based serialization at runtime, causing every tool to return `"An error occurred invoking '<tool>'."`. The `[UnconditionalSuppressMessage("Trimming", "IL2026")]` attribute suppressed the build warning but did not prevent the runtime crash. Trimming will be restored in v0.4.0 via source-generated serialization.

### Changed

- Binary size for Linux/macOS RIDs increased temporarily (~30 MB vs ~15 MB trimmed). Trimmed size returns in v0.4.0.

## [0.3.1] - 2026-07-23

### Changed

- **Migrated test framework to xunit.v3** (`xunit` 2.9.3 → `xunit.v3` 3.2.2). The Core.Tests project now emits as an executable (`OutputType=Exe`), and integration tests pass `TestContext.Current.CancellationToken` to async SqlClient calls per xunit.v3's cancellation model.
- **Removed the 3-argument `SqlExecutor` constructor.** The backward-compat overload that defaulted retry parameters to zero is gone; all callers now pass `retryCount`, `retryIntervalMin`, and `retryIntervalMax` explicitly. All integration test call sites updated.
- **Bumped `Microsoft.SqlServer.TransactSql.ScriptDom`** from 180.37.3 to 180.59.2.
- **Documentation cleanup**: removed stale references to the deprecated `mssql-mcp-cli` npm package, the old `install.js` postinstall script, and the "sqz" pattern name across README, ADRs, SECURITY.md, CONTRIBUTING.md, and release pipeline comments. The comparison table in README was simplified to a single-column feature list.

### Fixed

- Corrected the `[0.3.0]` changelog date from `2025-07-23` to `2026-07-23`.

## [0.3.0] - 2026-07-23

### Changed

- **Binary delivery moved from `postinstall` to `optionalDependencies`.** The npm package `@codegiveness/mssql-mcp` now ships as a thin main package with 5 per-platform `optionalDependencies` (`@codegiveness/mssql-mcp-linux-x64`, `-linux-arm64`, `-osx-x64`, `-osx-arm64`, `-win-x64`). npm's dependency resolution installs the correct binary automatically — no lifecycle script involved. This works even with `--ignore-scripts` and `--ignore-scripts=true` corporate configs. See [ADR-0028](docs/adr/0028-binary-delivery-via-optional-dependencies-and-shim-self-heal.md).

- **Shim self-heals from GitHub Releases.** When an optional dependency is absent (`--no-optional`, corporate mirrors), `bin/mssql-mcp.js` downloads the matching RID archive from GitHub Releases, verifies the SHA256 checksum, extracts to `~/.mssql-mcp/bin/<version>/<rid>/`, and caches it. Subsequent invocations hit the cache. `MSSQL_MCP_NO_DOWNLOAD=1` disables the download for locked-down environments.

- **Package renamed to `@codegiveness/mssql-mcp`.** The scoped name matches the invocation name.

- **NuGet version now auto-synced from the git tag.** The release pipeline writes `<VersionPrefix>` from the tag, preventing the version drift that caused NuGet to stay at 0.1.0 when npm was at 0.2.0.

### Removed

- `npm/install.js` — deleted. Its download/checksum/extract logic moved into `bin/mssql-mcp.js` (the shim), augmented with the cache + self-heal path.
- `npm/bin/mssql-mcp` (old Node shim) — deleted. Replaced by `npm/bin/mssql-mcp.js`.
- `postinstall` script — removed from `npm/package.json`.

### Added

- `npm/bin/mssql-mcp.js` — new shim with optionalDep resolution, self-heal download, SHA256 verification, cache, and a three-part error message contract (problem + GitHub Releases URL + `dotnet tool install` fallback).
- `npm/platforms/*/package.json` — 5 per-platform npm packages, each declaring `os` and `cpu` fields.
- CI `npm-pack` job: builds a real linux-x64 binary, stages it into the per-platform package, runs `npm pack` + `npm install --ignore-scripts`, and verifies `npx mssql-mcp --version` works. Also tests the `--no-optional` + `MSSQL_MCP_NO_DOWNLOAD=1` error path.
- Release pipeline: sequential 6-package publish (5 per-platform first, main last) with provenance.
- `CHANGELOG.md` — this file (ADR-0014 required it; the repo was missing it).
- [ADR-0028](docs/adr/0028-binary-delivery-via-optional-dependencies-and-shim-self-heal.md) — design record for the optionalDependencies + self-heal architecture.
- `CONTEXT.md` terms: **Postinstall-Independent Install**, **Harness Verification Record**.

### Fixed

- `npx -y @codegiveness/mssql-mcp` now works reliably. Previously, the `postinstall`-based `install.js` failed silently when npm skipped lifecycle scripts, leaving the shim as a dead-end error. The optionalDependencies approach eliminates this failure class entirely.

## [0.2.0]

### Changed

- `--validate` error classification (ADR-0027 Phase 2).
- CI smoke job: build + `--version` + `--validate` against Azure SQL Edge.
- CI README snippet lint script.
- `install.js` download-failure error classification (ADR-0027 Phase 2).
- Release pipeline: provenance + attestation re-enabled for public repo.

## [0.1.0]

### Added

- Initial release. 9 typed MCP tools for SQL Server, Guard with AST validation + read-only transactions + command timeout + byte-size safety net. Restricted and Unrestricted access modes. Self-contained single-file binaries for linux-x64, linux-arm64, osx-x64, osx-arm64. Framework-dependent binary for win-x64. NuGet tool package. npm package with postinstall-based binary delivery.
