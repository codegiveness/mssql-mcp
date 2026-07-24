# Architectural Decision Records

ADRs document every significant design choice in mssql-mcp. They are numbered sequentially and immutable once merged — superseded decisions are marked with a "Superseded by ADR-NNNN" link, never deleted.

| ADR | Title | Status |
|-----|-------|--------|
| [ADR-0001](0001-dual-access-mode.md) | Dual-mode access: Restricted (default) and Unrestricted (opt-in) | Active |
| [ADR-0002](0002-distribution-strategy.md) | Distribution: dotnet tool + npm wrapper | Active (install.js section superseded by ADR-0028) |
| [ADR-0003](0003-no-result-cap.md) | No application-layer row cap; transport-safety byte limit with notice | Active |
| [ADR-0004](0004-connection-lifecycle.md) | Single connection string at startup; rely on SqlClient built-in retry | Active |
| [ADR-0005](0005-authentication-matrix.md) | Authentication: SQL password + Windows Integrated + Active Directory Default | Active |
| [ADR-0006](0006-guard-ast-allowlist.md) | Guard AST allowlist: Visitor-based statement-type allowlist | Active |
| [ADR-0007](0007-restricted-execution-mechanics.md) | Restricted-mode execution: transaction + always rollback, configurable timeout | Active |
| [ADR-0008](0008-mcp-sdk-choice.md) | MCP SDK choice | Active |
| [ADR-0009](0009-return-shape-and-type-coercion.md) | Return shape and type coercion | Active |
| [ADR-0010](0010-error-handling.md) | Error handling | Active |
| [ADR-0011](0011-logging.md) | Logging | Active |
| [ADR-0012](0012-project-structure.md) | Project structure | Active |
| [ADR-0013](0013-testing-strategy.md) | Testing strategy | Active |
| [ADR-0014](0014-build-release-pipeline.md) | Build/release pipeline and stability contract | Active |
| [ADR-0015](0015-configuration-via-env-vars.md) | Configuration via environment variables | Active |
| [ADR-0016](0016-tool-input-schemas.md) | Tool input schemas (9 tools, v1) | Active |
| [ADR-0017](0017-execute-sql-annotations-1-0-0.md) | execute_sql annotations for 1.0.0 | Active |
| [ADR-0018](0018-graduation-triggers-reinterpretation.md) | Graduation triggers reinterpretation | Active |
| [ADR-0019](0019-nuget-provenance-skip-attestation-adopt-trusted-publishing.md) | NuGet provenance: skip attestation, adopt Trusted Publishing | Active |
| [ADR-0020](0020-roslyn-analyzer-strategy-hybrid-suppression.md) | Roslyn analyzer strategy: hybrid suppression | Active |
| [ADR-0021](0021-onboarding-surface-definition-of-worked.md) | Definition of "worked" for the onboarding surface | Active |
| [ADR-0022](0022-harness-coverage-scope.md) | Harness coverage scope | Active |
| [ADR-0023](0023-adopt-scoped-npm-package-name.md) | Adopt scoped npm package name | Active |
| [ADR-0024](0024-validate-proves-connection-troubleshooting-covers-harness-wiring.md) | --validate proves connection; troubleshooting covers harness wiring | Active |
| [ADR-0025](0025-readme-restructure-for-onboarding-first-flow.md) | README onboarding-first restructure | Active |
| [ADR-0026](0026-ci-tests-onboarding-surface.md) | CI tests onboarding surface | Active |
| [ADR-0027](0027-phased-release-onboarding-surface.md) | Phased release onboarding surface | Active |
| [ADR-0028](0028-binary-delivery-via-optional-dependencies-and-shim-self-heal.md) | Binary delivery via optional dependencies and shim self-heal | Active |
| [ADR-0029](0029-zero-config-hero-command-and-validate-placement.md) | Zero-config hero command and --validate placement | Active |
| [ADR-0030](0030-log-file-rotation.md) | Log file rotation | Active |
| [ADR-0031](0031-unknown-argument-dispatch-and-pre-push-discipline.md) | Unknown-argument dispatch and pre-push discipline | Active |
| [ADR-0032](0032-security-signaling-and-supply-chain-attestation.md) | Security signaling and supply-chain attestation | Active |
| [ADR-0033](0033-branch-protection-posture-for-solo-maintained-project.md) | Branch protection posture for solo-maintained project | Active |
