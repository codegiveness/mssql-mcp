# Security Posture

This page describes configured controls and their limits for mssql-mcp. Configuration is not proof that a workflow has run successfully or that a distribution artifact was attested. For vulnerability reporting, see [SECURITY.md](../SECURITY.md). For the current twelve Scorecard findings, evidence requirements, dependency-lock maintenance, and unresolved prerequisites, see [Security and quality follow-up](security-quality-follow-up.md).

## Runtime Guard

Restricted mode combines application-level checks with database permissions; it is not a substitute for a least-privilege SQL login:

1. **AST allowlist** — ScriptDom parses T-SQL before execution. Non-SELECT statements, SELECT-INTO and sequence-allocation expressions (`NEXT VALUE FOR`) are rejected, along with the existing dangerous reference constructs. Only the explicit Unrestricted mode bypasses the execution Guard; startup accepts only the two documented case-insensitive names, not numeric/combined enum values. Parser or policy defects remain possible.
2. **Rollback wrapper** — Guard-accepted SQL is wrapped in `BEGIN TRANSACTION ... ROLLBACK TRANSACTION`. This is defense in depth for transactional effects, not a SQL Server read-only authorization boundary. It cannot guarantee that arbitrary SQL, external side effects, or a Guard bypass are harmless. The operator must restrict the login's grants.
3. **Command timeout** — the default 30-second Restricted-mode command timeout limits command execution; it is not an end-to-end request deadline or a CPU/memory quota. Retries, network delays, and cleanup can extend elapsed time.
4. **Result budget** — `MSSQL_MAX_RESULT_BYTES` defaults to 10 MB; zero disables it. Query readers stop retaining rows using a conservative JSON byte estimate, and truncated results carry a notice. Oversized raw plans are refused with `PLAN_TOO_LARGE` instead of returning broken partial XML. The budget is not a strict process RSS ceiling: a single cell, provider buffers, metadata, plan acquisition, and serialization still allocate.

See [ADR-0006: Guard AST allowlist](adr/0006-guard-ast-allowlist.md) for the full design.

File logging rejects linked/reparse components in the active path, its ancestors and rotation archives before opening/moving files. Use a private server-owned directory: portable pre-open checks are not atomic against concurrent path replacement. The demonstrated linked-parent bypass was repaired; rejection of a dangling final link already worked on the exercised Linux/.NET 10 environment.

## Supply chain controls

| Control | Configuration and limits |
|---|---|
| GitHub Actions pinning | Workflow actions use full commit SHAs; updates still require review of the selected commit. |
| Container pinning | Docker SDK/runtime images and the CI SQL Edge image use registry digests; pinning does not establish that the image is vulnerability-free. |
| npm provenance | Release publishing requests provenance with `npm publish --provenance`; consumers must verify the provenance of the actual published version. |
| NuGet Trusted Publishing | Release uses OIDC login to obtain a short-lived publishing credential. The NuGet.org trusted-publisher configuration is an external prerequisite. This is publishing authentication, **not package provenance**. |
| NuGet provenance | Deliberately unavailable for `.nupkg` under ADR-0019: NuGet.org repository signing changes the uploaded bytes. Do not describe this channel as provenance-attested. |
| Release archive and SBOM attestation | The release workflow requests attestations for archives, checksums, and the generated CycloneDX SBOM. A successful release run and verification of downloaded artifacts are required evidence. Checksums alone do not authenticate a compromised publisher. |
| OpenSSF Scorecard | The workflow uploads findings on `main` and weekly; the [live report](https://securityscorecards.dev/viewer/?raw=github.com/codegiveness/mssql-mcp) measures repository practices, not an absence of vulnerabilities. |

See [ADR-0019](adr/0019-nuget-provenance-skip-attestation-adopt-trusted-publishing.md) and [ADR-0032](adr/0032-security-signaling-and-supply-chain-attestation.md).

## Branch protection

ADR-0033 records required `build`/`validate` checks, admin enforcement, CODEOWNERS, stale-review dismissal, linear history, and blocked force pushes/deletion. These are repository settings and must be inspected in GitHub when assessing enforcement; source files alone cannot prove their live state.

The required approving-review count remains **0**, deliberately, for a solo-maintained repository. CODEOWNERS is not proof of independent review and a maintainer cannot independently approve their own change. Signed-commit enforcement is deferred. The Scorecard Code-Review finding remains a real limitation until a separate qualified reviewer participates and actual review history accumulates; no second-account rubber stamp is a remediation.

See [ADR-0033](adr/0033-branch-protection-posture-for-solo-maintained-project.md). This work does not authorize a 1.x release or change the zero-major publication policy.

## Secret scanning

GitHub repository API inspection for this follow-up reported secret scanning and push protection enabled, with **0 open secret-scanning alerts**. This is a point-in-time observation, not proof that every secret format is detectable or that previously leaked credentials are safe.

## Dependency management and static analysis

| Control | Configuration and evidence requirement |
|---|---|
| Dependabot | `.github/dependabot.yml` schedules dependency updates; repository API inspection reported security updates enabled and **0 open Dependabot vulnerability alerts**. |
| NuGet dependency locking | `Directory.Build.props` enables generated lock files, with separate per-RID locks for publish graphs. CI and release restore in locked mode; build/publish/pack reuse the restored graph. Commit real generated versions and content hashes, and verify all publish profiles before calling this complete. |
| .NET analyzers | `AnalysisLevel=latest-recommended` and warnings-as-errors are enabled. Six named analyzer rules remain suppressed under ADR-0020; this is not equivalent to running security-specific SAST. |
| CodeQL SAST | `.github/workflows/codeql.yml` selects `security-and-quality` for C#, JavaScript/TypeScript and Python, with SHA-pinned actions and locked C# dependencies. The active [native CodeQL ruleset](https://github.com/codegiveness/mssql-mcp/rules/24351321) requires analysis and blocks HIGH/CRITICAL security alerts on `main`, without bypass actors. Expanded hosted execution and triage remain pending deployment. |
| Fuzzing | `.github/workflows/fuzz.yml` runs SharpFuzz/libFuzzer against instrumented ScriptDom and Guard, with a separate crash-detection probe. Scheduled campaigns run for 600 seconds; main-only cache writes retain data-only corpora for subsequent campaigns. Local long-campaign and corpus-reuse evidence is recorded in the follow-up; hosted cache persistence remains unverified. |
| Secret scanning | Content-pinned Gitleaks scans full committed history and SQL/container credential patterns, with redacted metadata and scoped synthetic-fixture exceptions. Detector positives, negatives, deleted history and ignored `.env` behavior were exercised. Optional GitHub non-provider scanning and validity checks still report disabled; do not equate a successful settings API response with activation. |
| Workflow auditing | Content-pinned zizmor, actionlint and ShellCheck run tokenlessly on PR code. Local audits passed after removing credential persistence, release cache poisoning and shell-expression hazards. |
| Artifact vulnerability scanning | Content-pinned Trivy scans the actual Docker image, CycloneDX SBOM, Inspector/c8 lock and npm publisher runtime lock with fresh advisories, failing on HIGH/CRITICAL findings. Local scans passed, including both clean source-based npm runtime closures. Release scans precede publication; packaging dry-run does not establish registry/OIDC success. |
| Security regressions and coverage | PR CI is configured to run live SQL tests, npm security regressions and the locked official Inspector 2.9.0 stdio smoke. Coverlet.MTP and native V8 coverage publish source-level line/branch reports, not a cosmetic coverage threshold. New required-check enforcement awaits verified publication of these workflows. |

The existing `build`/`validate` requirements remain intact. Strict branch protection now also requires GitHub Actions checks `Analyze (csharp)`, `Analyze (javascript-typescript)` and `sharpfuzz`, whose successful current-main identities were inspected before enforcement. New `security-regressions`, `secrets`, `workflow-audit` and `artifact-audit` requirements await deployed, observed jobs; the blocked local rollout is not claimed as hosted protection.

## Security audits

| Date | Type | Report |
|---|---|---|
| 2026-07-22 | Pre-public | [docs/security-audits/2026-07-22-pre-public.md](security-audits/2026-07-22-pre-public.md) |
| 2026-07-24 | Post-hardening | [docs/security-audits/2026-07-24-post-hardening.md](security-audits/2026-07-24-post-hardening.md) |
| 2026-07-25 | Hardening batch | [docs/security-audits/2026-07-25-hardening-batch.md](security-audits/2026-07-25-hardening-batch.md) |

## Security hardening batch (2026-07-25)

The [2026-07-25 report](security-audits/2026-07-25-hardening-batch.md) records a focused sprint spanning the npm shim and core server, with inverted PoC tests and the then-current pre-push suite (437 unit tests, 10 checks). Those historical results are not evidence of today's branch or newly added workflow execution.

| Finding | Fix | PoC test |
|---|---|---|
| F1: extractTarGz path/symlink traversal | tar entry validation before extraction | `npm/poc-tests.js` PB3 |
| F2: Cross-DB authz gap | `HAS_DBACCESS(name) = 1` check in `ValidateDatabaseSql` | `tests/mssql-mcp.Tools.Tests/PocCrossDbAuthzTests.cs` CB1-CB4 |
| F3: FileLoggerProvider path traversal | Path validation (reject `..`, reject symlinks) | `tests/mssql-mcp.Core.Tests/PocLeakTests3.cs` PB2 |
| F5: npm cache poisoning | sha256 re-verify on cache hit | `npm/poc-tests.js` PB5 |
| F6: PasswordObfuscator partial leak | Regex rewrite (consume full value) | `tests/mssql-mcp.Core.Tests/PocObfuscationPartialLeakTests.cs` PA2-PA6 |
| F7: fetchUrl redirect host pinning | Allowlist (`github.com` + `*.githubusercontent.com`) | `npm/poc-tests.js` PB4 |

Behavior change: 6 discovery tools (`list_schemas`, `list_objects`, `get_object_details`, `analyze_indexes`, `get_top_queries`, `analyze_db_health`) now reject databases the SQL login cannot access because `ValidateDatabaseSql` requires `HAS_DBACCESS(name) = 1`. The error message is unchanged, so no client parsing logic needs to change.

See [ADR-0035: Security hardening batch](adr/0035-security-hardening-batch.md) for full details.

## CODEOWNERS

[.github/CODEOWNERS](../.github/CODEOWNERS) assigns `@codegiveness/mssql-mcp-maintainers` to every path, with extra precision for the Guard, ADRs, workflows, and npm distribution. Its enforcement depends on live branch rules and eligible reviewers; it does not establish independent review for maintainer-authored changes.

## OpenSSF Best Practices

Submission at [bestpractices.dev](https://bestpractices.dev/) remains a manual human task. [Issue #68](https://github.com/codegiveness/mssql-mcp/issues/68) is historical self-assessment work, not evidence of a granted badge. The [self-assessment](./ossf-best-practices-self-assessment.md) must be checked against actual controls before a human submits it; until the external service grants a badge, the CII-Best-Practices finding remains unresolved.

## Threat model

mssql-mcp sits between an AI agent and a SQL Server. The trust boundaries are:

1. **Agent → Server** (MCP stdio) — the agent sends tool calls as JSON-RPC over stdin/stdout.
2. **Server → SQL Server** (SqlClient TCP) — the server opens a pooled connection using the operator-provided connection string.
3. **Operator → Server** (CLI/env config) — the operator configures the server via environment variables and CLI flags at startup.

| Threat | Mitigation | Residual risk |
|--------|------------|---------------|
| Destructive SQL by agent | AST allowlist and rollback wrapper in Restricted mode; least-privilege SQL grants | Parser/Guard defects, nontransactional effects, external side effects, or excessive grants can defeat application-level protection. |
| Credential leak to agent/logs | Password-pattern redaction at error/log boundaries | Redaction covers recognized connection-string syntax, not every secret or encoding. Novel formats, new error paths, SQL text, and runtime memory can expose secrets. |
| Cross-DB read by agent | Least-privilege SQL login; discovery `database` parameters checked for existence, online/multi-user state, and `HAS_DBACCESS` | These checks do not restrict a login already authorized to read sensitive data or implement table/row authorization. |
| Supply-chain tampering | SHA-pinned actions/images, locked NuGet graphs, archive checksums, requested npm/archive provenance, generated SBOM | NuGet provenance is absent; compromised maintainers, tools, registries, or source dependencies remain trust risks. Each artifact's evidence must be verified. |
| Unrestricted mode misuse | Explicit opt-in; destructive tool hints communicate intent | Hints are not authorization. Unrestricted execution commits changes within the login's SQL Server permissions. |
| Result size DoS | Conservative reader budget, response truncation notice, refusal of oversized raw XML plans | No strict RSS/CPU bound; individual values, provider buffers, metadata, and parsing/serialization can still be large. A zero budget disables protection. |
| Query timeout DoS | Configurable command timeout and cancellation handling | Command timeout is not a total request deadline; retries, parsing, memory pressure, network behavior, and SQL Server work can consume additional resources. |

See the [security audit reports](security-audits/) for detailed findings (AHD-1 through AHD-3) and their resolutions.
