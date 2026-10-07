# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.5.7](https://github.com/codegiveness/mssql-mcp/compare/v0.5.6...v0.5.7) (2026-10-02)


### Bug Fixes

* modernize .NET toolchain and resource lifetimes ([5551ad7](https://github.com/codegiveness/mssql-mcp/commit/5551ad70de7e222f36b293ce63ff0fbaacfaf815))
* **quality:** confine fixture cleanup and settle expanded findings ([#153](https://github.com/codegiveness/mssql-mcp/issues/153)) ([3fecb48](https://github.com/codegiveness/mssql-mcp/commit/3fecb485f3a70dbf94abae34b1c551091a583292))
* remove restricted authentication and native SNI assets ([8def9ae](https://github.com/codegiveness/mssql-mcp/commit/8def9ae68138812858638a73a82c858c27c27681))
* **security:** verified scan gates and fail-closed runtime controls ([#150](https://github.com/codegiveness/mssql-mcp/issues/150)) ([de7648f](https://github.com/codegiveness/mssql-mcp/commit/de7648f22ba273013b7181d8d1a6c988afc906c6))

## [Unreleased]

### Changed

- Pin the stable .NET SDK to 10.0.401, retain `net10.0` on runtime 10.0.12, and pin C# 14 instead of the floating `latest` language setting. Align CI and Docker with this SDK.
- Remove the SqlClient Azure authentication extension and its trimming root to avoid the restricted native broker dependency. Microsoft Entra connection-string authentication is no longer supported; retain SqlClient 7.1.1, SQL password, and Windows Integrated Authentication.
- Select SqlClient's managed networking on Windows and exclude all native SNI assets from every build/publish/pack profile. Framework-dependent publishing alone did not prevent native dependency redistribution; keep the existing Windows runtime requirement.
- Regenerate all 24 portable, test, fuzz, and runtime-profile package locks against official NuGet content without weakening locked restore or hash validation.
- Reuse the SQL row scratch array per result set, and avoid UTF-8 accounting when the execution-plan byte limit is disabled.
- Use the existing Node runtime rather than Python for the official Inspector smoke's JSON handling; retain npm distribution and independently justified CI/security tooling.
- Update checksum-pinned actionlint to 1.7.12 and move remaining Node 20 workflow setup to Node 24 LTS.

### Fixed

- Register logging providers through DI factories so host disposal closes the file sink and console worker; use one resolved configuration instance for both options injection shapes.
- Dispose SQL resources asynchronously where supported, bound SHOWPLAN cleanup independently of request cancellation and an unlimited query timeout, and preserve cancellation when SqlClient reports it during metadata reads.
- Serialize SQL `decimal`/`numeric` values from `SqlDecimal` directly, preserving 38-digit precision without CLR decimal overflow.
- Require encryption in synthetic connection-validation failure fixtures without changing their error-classification and password-obfuscation assertions.

### Security

- Remove `Microsoft.Identity.Client.NativeInterop` and the Azure/MSAL authentication graph from all package-lock profiles with owner approval. Retain fail-closed release/CI redistribution guards against reintroduction; the broker-free graph no longer requires the former publication hold. This is not a general license audit.
- Refresh the pinned npm publishing runtime's `http-cache-semantics` dependency from 4.2.0 to 4.3.0, outside the affected range reported by CVE-2026-93748. Retain npm 12.2.0, source/runtime integrity checks, and the fail-closed release security gate.
- Backport the exact synthetic detector-prefix exception for the C# generator path so full-history scanning works before the tooling migration merges. Detector fixtures verify that generated credentials at that same path remain detected.

## [0.5.6](https://github.com/codegiveness/mssql-mcp/compare/v0.5.5...v0.5.6) (2026-10-02)


### Bug Fixes

* isolate mutable test fixtures from shared temporary paths ([#146](https://github.com/codegiveness/mssql-mcp/issues/146)) ([2003a61](https://github.com/codegiveness/mssql-mcp/commit/2003a61e1b715f4335919173e9efffd929859e06))
* settle 0.x memory hardening and security backlog ([#139](https://github.com/codegiveness/mssql-mcp/issues/139)) ([194be9b](https://github.com/codegiveness/mssql-mcp/commit/194be9b8c29cd6392aff9efa169cd5dd0722886e))
* synchronize all compiled, npm/platform and MCP registry version stamps to 0.5.6 before merging the Release PR.
* make the mandatory `scripts/mcp-smoke.sh` entrypoint executable in native Linux checkouts; a clean worktree reproduced `Permission denied` before the mode fix and passed all three checks afterward.

## [0.5.5](https://github.com/codegiveness/mssql-mcp/compare/v0.5.4...v0.5.5) (2026-09-04)


### Bug Fixes

* speed up startup and update dependencies ([#127](https://github.com/codegiveness/mssql-mcp/issues/127)) ([f8ec6a6](https://github.com/codegiveness/mssql-mcp/commit/f8ec6a6ccee68789ed49bd5098845867b6c3d49b))

## [0.5.4](https://github.com/codegiveness/mssql-mcp/compare/v0.5.3...v0.5.4) (2026-07-25)


### Bug Fixes

* **ci:** sync version stamps before dotnet publish in release.yml ([#105](https://github.com/codegiveness/mssql-mcp/issues/105)) ([#110](https://github.com/codegiveness/mssql-mcp/issues/110)) ([8e02ed3](https://github.com/codegiveness/mssql-mcp/commit/8e02ed3ed2119c36d51a259c0c1dd8d1e48a1c78))

## [0.5.3](https://github.com/codegiveness/mssql-mcp/compare/v0.5.2...v0.5.3) (2026-07-25)


### Bug Fixes

* sync version stamps to 0.5.2 (manifest was ahead of csproj/npm/server.json) ([#107](https://github.com/codegiveness/mssql-mcp/issues/107)) ([88f6f11](https://github.com/codegiveness/mssql-mcp/commit/88f6f11ad276059c0c46dbafef684852cff6b82d)), closes [#106](https://github.com/codegiveness/mssql-mcp/issues/106)
* update stale docs to reflect current code (ADRs, versions, test counts) ([#109](https://github.com/codegiveness/mssql-mcp/issues/109)) ([665e145](https://github.com/codegiveness/mssql-mcp/commit/665e145d765fd6de512d3d8fb8fa33db5bc3aad2))

## [0.5.2](https://github.com/codegiveness/mssql-mcp/compare/v0.5.1...v0.5.2) (2026-07-25)


### Bug Fixes

* correct release-please output name for dispatch ([#100](https://github.com/codegiveness/mssql-mcp/issues/100)) ([9c1d75d](https://github.com/codegiveness/mssql-mcp/commit/9c1d75d1b9406e837d84f6f48137ebdef17f2eec))
* **security:** harden 7 audit findings — npm shim, logging, cross-DB authz ([#102](https://github.com/codegiveness/mssql-mcp/issues/102)) ([7649383](https://github.com/codegiveness/mssql-mcp/commit/764938358e881ee50ed8aa933574e53f83c66cc7))

## [0.5.1](https://github.com/codegiveness/mssql-mcp/compare/v0.5.0...v0.5.1) (2026-07-25)


### Bug Fixes

* dispatch release.yml on release-please tag creation ([#99](https://github.com/codegiveness/mssql-mcp/issues/99)) ([02663d1](https://github.com/codegiveness/mssql-mcp/commit/02663d144f2caaa155535fc1f36dc1db9216e025))
* sync version stamps to 0.5.0 + skip consistency on release merges ([#96](https://github.com/codegiveness/mssql-mcp/issues/96)) ([a73c277](https://github.com/codegiveness/mssql-mcp/commit/a73c27726e5bc7a20c168fd0ece90d5a8576c159))
* use PAT for release-please to trigger downstream workflows ([#98](https://github.com/codegiveness/mssql-mcp/issues/98)) ([5302979](https://github.com/codegiveness/mssql-mcp/commit/5302979c4e254466cb07701a5b7a89a6790a45e6))

## [0.5.0](https://github.com/codegiveness/mssql-mcp/compare/v0.4.2...v0.5.0) (2026-07-25)


### Features

* release-please automated version stamping + consistency guard ([#86](https://github.com/codegiveness/mssql-mcp/issues/86)) ([7f660a8](https://github.com/codegiveness/mssql-mcp/commit/7f660a8842099079ea91969e74a53e21e63f887b)), closes [#78](https://github.com/codegiveness/mssql-mcp/issues/78)


### Bug Fixes

* **ci:** scope Scorecard write permissions to job level ([#73](https://github.com/codegiveness/mssql-mcp/issues/73)) ([79368ce](https://github.com/codegiveness/mssql-mcp/commit/79368ce927d880dd0e47a9380634bb1204d83b1f))
* **ci:** use dereferenced commit SHA for scorecard-action ([#74](https://github.com/codegiveness/mssql-mcp/issues/74)) ([d3a4467](https://github.com/codegiveness/mssql-mcp/commit/d3a4467410b41c83db0ab612d2524857effa8359))
* correct release-please manifest format + unified stamp sync ([#88](https://github.com/codegiveness/mssql-mcp/issues/88)) ([2f674cf](https://github.com/codegiveness/mssql-mcp/commit/2f674cf715aa46ea627890b2d20e8ac8c497517f)), closes [#78](https://github.com/codegiveness/mssql-mcp/issues/78)
* **readme:** update Scorecard badge URL to api.scorecard.dev ([#76](https://github.com/codegiveness/mssql-mcp/issues/76)) ([d00adef](https://github.com/codegiveness/mssql-mcp/commit/d00adef96d504078367166aa91cce92e871f09cb))
* skip version-consistency check for bot PRs ([#90](https://github.com/codegiveness/mssql-mcp/issues/90)) ([01dba32](https://github.com/codegiveness/mssql-mcp/commit/01dba32967a5c1e38a4407fb28d0d78e1cd2890c)), closes [#78](https://github.com/codegiveness/mssql-mcp/issues/78)

## [Unreleased]

### Fixed

- Bound query row accumulation using a conservative JSON-byte budget that includes keys, structure, nulls, escaping, and Unicode. Early reader termination now carries an explicit truncation notice; disabling the budget preserves uncapped behavior.
- Refuse oversized raw SHOWPLAN XML with structured `PLAN_TOO_LARGE` and `format=summary` recovery advice, using bounded XML-compatible reads rather than materializing the complete plan first.
- Align Microsoft.Extensions dependencies at 10.0.12, update SqlClient to 7.1.1 and coverlet to 10.1.0, and update the SHA-pinned Scorecard uploader.
- Incorporate the subsequent dependency PRs for Test SDK 18.10.1, SourceLink 10.0.401, ScriptDom 180.117.0, and xunit.v3 4.0.1 with regenerated portable, fuzz, and all six runtime lock graphs.
- Enable existing integration tests through an explicit runtime opt-in and repair their actual SQL result-type assumptions.
- Synchronize all npm platform manifests with the release version and verify local package installs offline.
- Pin Docker images to registry digests and use genuine content-hashed NuGet locks for portable and six runtime publish profiles. Disable the distro SDK's implicit repackaged dependency feed so official SDK and container restores validate the same hashes.
- Restore actual SQL functionality in the Alpine container by using the ICU-enabled .NET 10 runtime image instead of invariant globalization. CI now checks a live container SQL connection, not just version output.
- Replace source-text security assertions with behavioral redirect rejection/depth regressions; remove obsolete export-only smoke assertions.
- Isolate mutable server-metadata test fixtures in an atomically created private temporary directory, preventing a reproduced symlink overwrite; remove incidental formatting and retry-wiring assertions.
- Reject sequence-allocation expressions in Restricted mode and add an independent fuzz invariant; rollback cannot restore consumed sequence values.
- Reject linked log-path ancestors and rotation targets, with deterministic filesystem regressions; private server-owned directories remain necessary against path-replacement races.
- Reject numeric/combined access-mode configuration and require explicit Unrestricted mode before write routing, preventing mismatched read-only hints and committed execution.
- Clear earlier vulnerability reports before reruns so scanner failures cannot present stale findings as current evidence.
- Confine traversal-regression teardown to an atomically created owned directory, preserving pre-existing shared temporary files; remove overwritten parser-list allocations and incidental query-source/default/mock-echo assertions, while retaining wire-contract tests and intentional error boundaries.

### Added

- C# and JavaScript/TypeScript CodeQL security analysis and a bounded, coverage-guided ScriptDom/Guard fuzzing workflow with an intentional engine-crash probe and retained campaign artifacts.
- Current security finding dispositions and explicit external prerequisites; historical audits remain unchanged.
- Full-history SQL/provider credential scanning with redacted reports, content-pinned workflow auditors, and fresh-advisory Docker/SBOM/npm vulnerability gates.
- Live SQL, npm security and official MCP Inspector proofs in PR CI, production branch-coverage reports, and 600-second scheduled fuzzing with main-only retained-corpus writes.
- Canonical `.bestpractices.json` service proposals with evidence-backed statuses and unknown personal/TLS attestations, rather than a falsely certified external badge.
- Link the README to the maintainer's live OpenSSF Best Practices project 15156 badge, including its in-progress state rather than claiming a passing award.
- Reconcile public vulnerability identifiers with their fixing releases and add dated security clarifications without changing tags/artifacts; resolve the release-note assessment proposal and record merged-main checks, actual retained-corpus reuse and the remaining zero-approval Scorecard finding.

### Changed

- Continue releases on 0.x. Release automation rejects major versions 1 and above; superseded v1 graduation tasks are retired rather than falsely certified.
- Record the maintainer's successful production use with Oh My Pi and OpenCode as user-reported evidence, without inventing a 30-day log or verification of other clients.
- Expand CodeQL to `security-and-quality` for C#, JavaScript/TypeScript and Python; activate a native HIGH/CRITICAL CodeQL merge gate without bypass actors. Exercise hosted analyses, triage complete SARIF and require all 15 observed GitHub Actions checks with strict branch protection.
- Content-lock Inspector 2.9.0/c8 and install unchanged hash-pinned npm 12.2.0 source with independently SRI-locked production dependencies; resolve the original bundled HIGH vulnerabilities without vendor patches or scan exceptions. Adapt MCP smoke to the v2 response envelope and memory-only secret storage.
- Switch npm release authentication to package-scoped GitHub Trusted Publishing (OIDC), with Node 24 and a content-locked npm CLI; remove the workflow's long-lived `NPM_TOKEN` dependency while preserving provenance, sequential platform-first publication, and the 0.x guard. Publication requires each package's trusted-publisher authorization and a passing tool-dependency scan.

## [0.4.2] - 2026-07-24

### Added

- **Security hardening (post-audit).** Credential leak fixes (AHD-2, AHD-3) — `PasswordObfuscator` now applied at every error boundary, including `ConnectionError`, `Internal`, and `ConnectionValidator` paths. See [docs/security-audits/2026-07-24-post-hardening.md](docs/security-audits/2026-07-24-post-hardening.md) for the full audit.
- **SBOM (CycloneDX).** Generated in CI, attested via `actions/attest@v4`, and attached to GitHub Releases.
- **`server.json`** — MCP server manifest for discoverability by MCP registries.
- **`docs/security-posture.md`** — consolidated security evidence page (OpenSSF Scorecard, SBOM, branch protection, supply-chain attestation, threat model).
- **README badges** — OpenSSF Scorecard, SBOM, Security Policy.
- **`Dockerfile`** — containerized deployment for Linux x64 (self-contained).

### Fixed

- **Scorecard badge URL** — corrected to `api.scorecard.dev` endpoint.
- **Scorecard CI** — dereferenced commit SHA for `scorecard-action`, scoped write permissions to job level.

## [0.4.1] - 2026-07-24

### Added

- **`--help`/`-h` flag** — prints a usage block listing all flags, env var equivalents, defaults, and a "To update" section (npm + dotnet tool update paths). Exits 0. Previously, running `mssql-mcp --help` produced a confusing "Missing SQL Server connection string" error.
- **Graceful unknown-argument error** — unrecognized arguments (e.g. `mssql-mcp upgrade`) now print `mssql-mcp: unknown argument '<arg>'.` + the usage block to stderr and exit 1. Previously, unknown args fell through to `MssqlMcpOptions.Parse()` and threw a connection-string error that had nothing to do with the user's intent.
- **Pre-push verification discipline** — `CONTRIBUTING.md` and `AGENTS.md` now carry a pre-push checklist table (unit tests, integration tests, `--validate`, `--help`, unknown-arg error, npm smoke, LSP diagnostics, MCP stdio smoke). `.env.example` is committed as a template with angle-bracket placeholders.
- **MCP stdio smoke test** — `scripts/mcp-smoke.sh` uses the official MCP Inspector CLI to perform a real JSON-RPC handshake (`initialize` → `tools/list` → `tools/call list_databases`). Added to the pre-push checklist as mandatory.
- **ADR-0031** — records the decision rationale for the unknown-argument dispatch layer and pre-push discipline.

### Fixed

- **`get_object_details` returned `[]` for every object** — three bugs in `DatabaseTools.cs`:
  1. `sys.objects.type` is `char(2)`, returning `"U "` (padded). The code compared to `"U"` without trimming — `Trim()` the value after reading.
  2. `sys.columns` and `sys.parameters` don't have a `system_type_name` column — join to `sys.types` on `user_type_id` to get the type name.
  3. `sys.objects.object_id` is `int` (Int32), not `long` (Int64). The `is long` check failed silently, defaulting `objectId` to `0L`, so all detail queries matched nothing — handle both `int` and `long`.

### Changed

- **`Program.cs` now dispatches via `CliDispatch`** — a pure function in Core decides Version/Help/UnknownArgument/RunServer before `MssqlMcpOptions.Parse()` runs. `Parse()` is unchanged. Precedence: Help > Version > UnknownArgument > RunServer.

## [0.4.0] - 2026-07-23

### Fixed

- **Permanently fixed issue #44 via source-generated JSON serialization.** All `System.Text.Json` serialization now uses `McpJsonContext` (a `JsonSerializerContext` subclass) with explicit `[JsonSerializable]` registrations for every DTO and primitive type. `PublishTrimmed=true` is restored on Linux/macOS builds — the trimming that previously crashed `System.Text.Json` reflection-based serialization no longer affects the codebase because no reflection-based serialization remains. Anonymous error-payload types are replaced by explicit `record` DTOs (`GuardRejectionPayload`, `TimeoutPayload`, `SqlErrorPayload`, `InternalErrorPayload`, `ConnectionErrorPayload`, `ObjectNotFoundPayload`, `DmlStatusPayload`, `DdlStatusPayload`, `QueryPlanSummary`, `QueryPlanOperation`, `MissingIndexPayload`). Byte-for-byte JSON equality is verified by `DtoJsonEqualityTests` (24 test methods). All `[UnconditionalSuppressMessage("Trimming", "IL2026")]` attributes are removed from production code.

### Changed

- **Trimmed binary size is ~30 MB** (same as v0.3.2's untrimmed size). While trimming is re-enabled, the source-generated `McpJsonContext` registers `object` and `Dictionary<string,object?>` for polymorphic row serialization, which forces the trimmer to retain more metadata than the original reflection-based approach. The binary is functional and unblocks all Linux/macOS users. A future ticket may narrow the type set to recover the ~15 MB size.
- **New release smoke step**: the release pipeline now invokes `list_databases` via JSON-RPC over stdio against Azure SQL Edge and fails the release if the response contains the crash envelope (`"An error occurred invoking '<tool>'."`). This regression guard would have caught issue #44 before any user hit it.
- **ADR-0009** amended: documents the closed-set invariant — the coercion layer's output set (`string`, `int`, `long`, `double`, `bool`, `null`) is registered in `McpJsonContext`, and future Sql* type additions to `Coerce` MUST add a matching `[JsonSerializable]` entry.
- **ADR-0010** amended: error envelopes are now explicit `record` DTOs rather than anonymous types. JSON shape is unchanged.
- **ADR-0014** amended: records the `PublishTrimmed=true` → `false` (v0.3.2) → `true` (v0.4.0) journey and the new JSON-RPC smoke step.

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
