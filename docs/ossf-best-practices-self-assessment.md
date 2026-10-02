# OpenSSF Best Practices Self-Assessment

This document is a draft self-assessment against the OpenSSF Best Practices passing-level criteria for mssql-mcp. Each criterion lists its status (Met / Unmet / N/A) and a one-line evidence citation. The human maintainer must still submit the project to [bestpractices.dev](https://bestpractices.dev/) to make the badge official.

Evidence refreshed on 2026-10-02 during [settlement PR #139](https://github.com/codegiveness/mssql-mcp/pull/139). Statuses are a draft, not an external award or a guarantee against vulnerabilities. [Issue #140](https://github.com/codegiveness/mssql-mcp/issues/140) tracks human review/submission. Criteria are interpreted against the [actual passing requirements](https://www.bestpractices.dev/en/criteria/0), not inferred from their identifiers.

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
- **report_tracker** — Met — GitHub Issues are enabled; #140 tracks actual external assessment submission, while historical #68 completed repository documentation/badge wiring
- **report_responses** — Met — API inventory on 2026-10-02 found 77 owner-authored issues and no non-owner/non-bot reports; there was no external bug-report response sample to measure. A published SLA alone is not evidence of response performance
- **enhancement_responses** — Met — The same public inventory contained no external-author enhancement requests; CONTRIBUTING.md documents the request process. Reassess when an actual response sample exists
- **report_archive** — Met — GitHub Issues and Security Advisories archive reports; docs/security-audits/ archive point-in-time audits
- **vulnerability_report_process** — Met — SECURITY.md:1-7 documents private GitHub vulnerability reporting and forbids public issues
- **vulnerability_report_private** — Met — SECURITY.md:5-7 explicitly uses GitHub's private vulnerability reporting and says "Do not open a public issue"
- **vulnerability_report_response** — N/A — Repository advisory API returned no records on 2026-10-02, so no report-response interval was available to assess. Verify any privately received reports before submission; the published SLA is not execution evidence

## Quality

- **build** — Met — `dotnet build mssql-mcp.sln` builds all src and test projects; CI runs it on every push/PR
- **build_common_tools** — Met — .NET SDK 10, `dotnet restore`, `dotnet build`, `dotnet pack`, `dotnet test` are standard .NET tooling
- **build_floss_tools** — Met — Build uses only standard .NET SDK and open-source Node tooling; all build tooling is free/libre
- **test** — Met — Current observed run: 439 unit cases passed; the full opt-in disposable SQL Server run passed 464 cases, with 0 failures and 4 existing unreachable-case skips in each. Commands and limits are recorded in docs/security-quality-follow-up.md
- **test_invocation** — Met — CONTRIBUTING.md:46-47 documents how to run unit and integration tests
- **test_most** — Met — Actual merged unit/live-SQL Coverlet 10.1.0 measurement: Core 81.16% line / 80.51% branch; Tools 86.68% line / 67.73% branch. These are the two measured C# libraries, not whole-repository/CLI/JavaScript coverage or proof of all input combinations
- **test_continuous_integration** — Met — CI runs build, format, unit, offline npm, native CLI/SQL and live container SQL checks on push/PR. The real MCP stdio Inspector check is separately mandatory before pushes; do not claim it runs in hosted CI
- **test_policy** — Met — CONTRIBUTING.md:64-79 pre-push checklist mandates passing tests; ADR-0031 documents unknown-argument dispatch and pre-push discipline
- **tests_are_added** — Met — CONTRIBUTING.md:148-149 requires new code to have unit tests; PR checklist enforces it
- **tests_documented_added** — Met — CONTRIBUTING.md:148-149 and PR checklist require tests for new code; tests follow project layout naming
- **warnings** — Met — Directory.Build.props uses `TreatWarningsAsErrors=true` and `AnalysisLevel=latest-recommended`
- **warnings_fixed** — Met — CI `dotnet build` and `dotnet format` are clean; security audit confirms warnings treated as errors
- **warnings_strict** — Met — Warnings are errors and latest-recommended .NET analyzers are enabled, with six explicit, documented suppressions in ADR-0020; strictness is practical, not a claim that every possible rule is enabled

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
- **crypto_password_storage** — N/A — The project does not store password verifiers for authentication of external users. Operator SQL credentials are a different concern; environment configuration is recommended, and the supported CLI flag does not establish an absence of argv exposure
- **crypto_random** — N/A — No custom randomness required for project logic
- **delivery_mitm** — Met — GitHub archive/checksum downloads and npm/NuGet registry delivery use HTTPS. Archive checksums, requested attestations and npm provenance are additional controls whose actual per-release evidence must be checked
- **delivery_unsigned** — Met — The npm shim retrieves checksum sidecars over HTTPS, not unsigned HTTP. This criterion is not a claim that every package has artifact provenance; NuGet Trusted Publishing authenticates publication and is not artifact provenance
- **vulnerabilities_fixed_60_days** — Met — Documented pre-public/post-hardening audit fixes shipped promptly; the 2026-10-02 public inventory found no medium/high/critical-labeled unresolved reports and no advisory records. Review released fixes and any additional reports before submission; a 90-day disclosure policy is not itself proof of this 60-day criterion
- **vulnerabilities_critical_fixed** — Met — All critical/high findings from pre-public and post-hardening audits were fixed before public release; see docs/security-audits/
- **no_leaked_credentials** — Met — The 2026-10-02 repository secret-scanning API returned zero open alerts, with scanning and push protection enabled. This is a point-in-time repository observation, not proof of absence or a claim that runtime redaction covers every secret format

## Analysis

- **static_analysis** — Met — Actual [CodeQL C#/JavaScript analyses](https://github.com/codegiveness/mssql-mcp/actions/runs/36962352592) completed and uploaded results. The fresh-main scan reported six findings; their traces, confirmed temporary-file fix, and false-positive evidence are recorded in [the follow-up](security-quality-follow-up.md#merged-main-findings-and-disposition). Roslyn/format checks and Scorecard are separate controls
- **static_analysis_common_vulnerabilities** — Met — CodeQL uses security-extended queries; uploaded records reported 63 C# and 103 JavaScript/TypeScript rules. These rule counts do not establish complete security coverage
- **static_analysis_fixed** — Met — Confirmed unsafe temporary fixture writes were reproduced and repaired with an atomically created private directory; SQL findings were traced to benign test paths and actual provider encryption was checked. Fresh-main scan confirmation remains the closure gate. Historical audit fixes and accepted residual risks remain documented; future confirmed medium/higher exploitable findings still require timely fixes
- **static_analysis_often** — Met — CodeQL runs on main pushes, PRs and weekly; Roslyn/format checks run in CI. A new workflow does not retroactively scan historical commits
- **dynamic_analysis** — Met — Actual [hosted SharpFuzz/libFuzzer run](https://github.com/codegiveness/mssql-mcp/actions/runs/36960543729) instrumented ScriptDom/Guard, detected the separate deliberate engine crash, then completed 370,328 real inputs in 61 seconds with feature/corpus growth and no emitted product findings
- **dynamic_analysis_unsafe** — N/A — Shipping project code is C#/JavaScript, not a memory-unsafe language. The nonshipping upstream C++ fuzz bridge does not establish sanitizer coverage of all native dependencies
- **dynamic_analysis_enable_assertions** — Met — The fuzz target has unconditional malformed/empty-acceptance, non-SELECT and SELECT-INTO invariants that throw even in Release builds; the independent crash probe proves exceptions reach the engine
- **dynamic_analysis_fixed** — Met — The confirmed temporary-file symlink overwrite was reproduced before and prevented after the fix. Parser/Guard fuzz campaigns emitted no product findings; the separate intentional engine-crash probe is not a vulnerability. Future confirmed findings still require timely fixes
