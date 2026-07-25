# OpenSSF Best Practices Self-Assessment

This document is a draft self-assessment against the OpenSSF Best Practices passing-level criteria for mssql-mcp. Each criterion lists its status (Met / Unmet / N/A) and a one-line evidence citation. The human maintainer must still submit the project to [bestpractices.dev](https://bestpractices.dev/) to make the badge official.

## Basics

- **description_good** — Met — README.md:1-3 (project name, tagline, and summary of what mssql-mcp does)
- **interact** — Met — README.md supported-clients section lists MCP harness wiring for Claude Desktop, Cursor, VS Code, Windsurf, Cline, Continue, opencode, Codex CLI, Gemini CLI, Antigravity, Hermes, Kiro, Zed
- **contribution** — Met — CONTRIBUTING.md:1-3 describes how to contribute, PR process, and standards
- **contribution_requirements** — Met — CONTRIBUTING.md:143-164 lists PR checklist, Conventional Commits, branch protection, and code-review expectations
- **floss_license** — Met — LICENSE is MIT; README.md Trademarks & licensing section links to LICENSE
- **floss_license_osi** — Met — MIT is OSI-approved; LICENSE file contains standard MIT text
- **license_location** — Met — LICENSE at repository root; README.md and docs/security-posture.md link to it
- **documentation_basics** — Met — README.md covers install, configuration, tools, examples, troubleshooting, security, development; docs/adr/README.md indexes 35 ADRs
- **documentation_interface** — Met — README.md Tools section and ADR-0016 document the 9 MCP tool schemas and return shapes
- **sites_https** — Met — All distribution URLs and badge links in README.md use https://github.com, https://nuget.org, https://npmjs.com, https://bestpractices.dev
- **discussion** — Met — README.md Contributing section points to GitHub Issues for bugs/features; SECURITY.md points to private vuln reporting
- **english** — Met — README.md, CONTRIBUTING.md, SECURITY.md, and all ADRs are written in English
- **maintained** — Met — Recent commits include security audits (2026-07-22 and 2026-07-24), scorecard.yml workflow, and active branch protection; README stability section shows 0.x active maintenance

## Change Control

- **repo_public** — Met — GitHub repository is public at github.com/codegiveness/mssql-mcp
- **repo_track** — Met — Git repository is the authoritative source; all changes are committed and pushed
- **repo_interim** — Met — No opaque intermediary; commits go directly to GitHub via PR workflow
- **repo_distributed** — Met — Git is a distributed VCS; every clone has full history
- **version_unique** — Met — release.yml uses Git tags as source of truth; npm and NuGet versions are synced to the tag via scripts
- **version_semver** — Met — Tags use `v*.*.*` (Semantic Versioning); README stability section references 0.x series
- **version_tags** — Met — release.yml triggers on `push: tags: 'v*.*.*'` and release-please creates versioned release PRs
- **release_notes** — Met — release.yml uses `gh release create --generate-notes` for GitHub Release notes; release-please generates CHANGELOG entries
- **release_notes_vulns** — Met — SECURITY.md:32-38 includes vulnerability disclosure SLA and release note expectations

## Reporting

- **report_process** — Met — README.md Contributing section and SECURITY.md:5-7 describe GitHub Issues for bugs and private vulnerability reporting
- **report_tracker** — Met — GitHub Issues are enabled; issue #68 tracks OpenSSF Best Practices submission
- **report_responses** — Met — SECURITY.md:34-38 publishes response SLAs (48h acknowledge, 14d assessment, 90d fix/disclosure)
- **enhancement_responses** — Met — CONTRIBUTING.md:167-169 asks for GitHub Issues with use-case descriptions
- **report_archive** — Met — GitHub Issues and Security Advisories archive reports; docs/security-audits/ archive point-in-time audits
- **vulnerability_report_process** — Met — SECURITY.md:1-7 documents private GitHub vulnerability reporting and forbids public issues
- **vulnerability_report_private** — Met — SECURITY.md:5-7 explicitly uses GitHub's private vulnerability reporting and says "Do not open a public issue"
- **vulnerability_report_response** — Met — SECURITY.md:34-38 defines acknowledgment, assessment, and fix/disclosure SLAs

## Quality

- **build** — Met — `dotnet build mssql-mcp.sln` builds all src and test projects; CI runs it on every push/PR
- **build_common_tools** — Met — .NET SDK 10, `dotnet restore`, `dotnet build`, `dotnet pack`, `dotnet test` are standard .NET tooling
- **build_floss_tools** — Met — Build uses only standard .NET SDK and open-source Node tooling; all build tooling is free/libre
- **test** — Met — CONTRIBUTING.md:46-47 and CI run `dotnet test --filter Category!=Integration`; ~440 unit tests
- **test_invocation** — Met — CONTRIBUTING.md:46-47 documents how to run unit and integration tests
- **test_most** — Met — tests/ directory covers Guard AST validation, tool wiring, error handling, type coercion, and regression tests for security findings
- **test_continuous_integration** — Met — .github/workflows/ci.yml runs build, format check, unit tests, npm smoke, Docker build, and MCP stdio smoke on every push/PR
- **test_policy** — Met — CONTRIBUTING.md:64-79 pre-push checklist mandates passing tests; ADR-0031 documents unknown-argument dispatch and pre-push discipline
- **tests_are_added** — Met — CONTRIBUTING.md:148-149 requires new code to have unit tests; PR checklist enforces it
- **tests_documented_added** — Met — CONTRIBUTING.md:148-149 and PR checklist require tests for new code; tests follow project layout naming
- **warnings** — Met — Directory.Build.props uses `TreatWarningsAsErrors=true` and `AnalysisLevel=latest-recommended`
- **warnings_fixed** — Met — CI `dotnet build` and `dotnet format` are clean; security audit confirms warnings treated as errors
- **warnings_strict** — Unmet — Additional external analyzers (e.g., security-focused Roslyn analyzers) are deliberately deferred per ADR-0020

## Security

- **know_secure_design** — Met — ADR-0006 (Guard AST allowlist), ADR-0007 (transaction rollback), ADR-0015 (secrets in env), ADR-0032 (supply-chain attestation) document threat-aware design
- **know_common_errors** — Met — SECURITY.md threat model lists destructive SQL, credential leak, cross-DB read, supply-chain tampering, result DoS, query timeout DoS with mitigations; ADR-0020 discusses common error classes
- **crypto_published** — N/A — mssql-mcp is a database connector; it does not implement custom cryptographic protocols
- **crypto_call** — N/A — SQL Server TLS is handled by the operator-provided connection string and SqlClient; no project-owned crypto calls
- **crypto_floss** — N/A — No custom crypto implementation exists in the project
- **crypto_keylength** — N/A — No project-managed keys or cryptographic primitives
- **crypto_working** — N/A — No custom crypto implementation; connection encryption is delegated to SQL Server / SqlClient
- **crypto_weaknesses** — N/A — No custom crypto to review; documented risk: README and SECURITY.md warn about `TrustServerCertificate=True`
- **crypto_pfs** — N/A — No project-managed key exchange; TLS settings are operator-controlled in the connection string
- **crypto_password_storage** — Met — Connection strings are never stored; passed via `MSSQL_CONNECTION_STRING` env var, never argv, per ADR-0015 and README Configuration section
- **crypto_random** — N/A — No custom randomness required for project logic
- **delivery_mitm** — Met — Release archives have SHA256 sidecars; npm publish uses `--provenance`; release.yml attests archives with `actions/attest@v4`; NuGet uses Trusted Publishing via OIDC
- **delivery_unsigned** — Met — GitHub Release artifacts are attested; npm and NuGet packages are published with provenance/Trusted Publishing; checksum sidecars shipped with archives
- **vulnerabilities_fixed_60_days** — Met — SECURITY.md:34-38 commits to fix or disclose within 90 days of assessment; two security audits (2026-07-22 and 2026-07-24) fixed all blocking findings promptly
- **vulnerabilities_critical_fixed** — Met — All critical/high findings from pre-public and post-hardening audits were fixed before public release; see docs/security-audits/
- **no_leaked_credentials** — Met — PasswordObfuscator scrubs `Password=`, `PWD=`, `AccessToken=`, `Token=` patterns; AHD-2 and AHD-3 regression tests confirm credential leaks fixed

## Analysis

- **static_analysis** — Met — .github/workflows/scorecard.yml runs OpenSSF Scorecard weekly; Directory.Build.props enables latest-recommended Roslyn analyzers; `dotnet format` runs in CI
- **static_analysis_common_vulnerabilities** — Unmet — External security analyzers (e.g., CodeQL, Semgrep) are not configured; deferred per ADR-0020
- **static_analysis_fixed** — Met — All blocking findings from two security audits were remediated; remaining items are documented accepted risks
- **static_analysis_often** — Met — Scorecard workflow runs weekly; Roslyn analyzers and `dotnet format` run on every CI build
- **dynamic_analysis** — Unmet — No dynamic/fuzz testing is currently integrated in CI; deferred as accepted risk for a database-connector project
- **dynamic_analysis_unsafe** — Unmet — Depends on dynamic_analysis; no unsafe-input dynamic tests are run
- **dynamic_analysis_enable_assertions** — N/A — .NET `Debug.Assert` is not used as a runtime safety mechanism; tests use xUnit assertions
- **dynamic_analysis_fixed** — N/A — No dynamic analysis findings to remediate because dynamic analysis is not currently performed
