# Security Posture

This page consolidates all security evidence and controls for mssql-mcp in one place. For vulnerability reporting, see [SECURITY.md](../SECURITY.md).

## Runtime Guard

Restricted mode applies four layers of defense so an AI agent can query SQL Server without a human in the loop:

1. **AST allowlist** — T-SQL is parsed by Microsoft.SqlServer.TransactSql.ScriptDom, then walked by a Visitor that allows only `SelectStatement` (including CTEs). Nested statements inside `BEGIN...END`, `IF`, and `WHILE` are also inspected. `INTO`, `OPENROWSET`, `EXECUTE AS`, DDL, and DML are rejected.
2. **Read-only transactions** — every `execute_sql` runs inside `BEGIN TRAN ... ROLLBACK`, so even an allowlist bypass cannot commit changes.
3. **Command timeout** — the default 30-second timeout in Restricted mode kills runaway queries.
4. **Byte-size safety net** — results over `MSSQL_MAX_RESULT_BYTES` (default 10 MB) are truncated with a notice, protecting the agent's context window.

See [ADR-0006: Guard AST allowlist](adr/0006-guard-ast-allowlist.md) for the full design.

## Supply chain attestation

| Control | Status | Evidence |
|---|---|---|
| GitHub Actions SHA-pinned | ✅ | All `uses:` in workflows are pinned to 40-char commit SHAs |
| npm provenance | ✅ | `npm publish --provenance` in release.yml |
| NuGet Trusted Publishing | ✅ | OIDC-based, no long-lived API key (ADR-0019) |
| Release archive attestation | ✅ | `actions/attest@v4` on release archives + checksums |
| SBOM (CycloneDX) | ✅ | Generated in CI, attested and attached to GitHub Releases |
| OpenSSF Scorecard | ✅ | Weekly scan, results in Security tab, [live badge](https://securityscorecards.dev/viewer/?raw=github.com/codegiveness/mssql-mcp) |

See [ADR-0019: NuGet Trusted Publishing](adr/0019-nuget-provenance-skip-attestation-adopt-trusted-publishing.md) and [ADR-0032: Security signaling and supply-chain attestation](adr/0032-security-signaling-and-supply-chain-attestation.md).

## Branch protection

| Control | Status |
|---|---|
| Required status checks (build, validate) | ✅ strict |
| Enforce admins | ✅ |
| Code owner reviews required | ✅ |
| Dismiss stale reviews | ✅ |
| Required approving reviews | 0 (deliberate — solo maintainer, see ADR-0033) |
| Linear history | ✅ |
| Force pushes blocked | ✅ |
| Branch deletion blocked | ✅ |
| Signed commits required | ❌ Deferred (see ADR-0033) |

See [ADR-0033: Branch protection posture for solo-maintained project](adr/0033-branch-protection-posture-for-solo-maintained-project.md).

## Secret scanning

| Control | Status |
|---|---|
| Secret scanning | ✅ enabled |
| Push protection | ✅ enabled |

## Dependency management

| Control | Status | Evidence |
|---|---|---|
| Dependabot (GitHub Actions) | ✅ weekly | `.github/dependabot.yml` |
| Dependabot (NuGet) | ✅ monthly | `.github/dependabot.yml` |
| Dependabot security updates | ✅ enabled | Repo settings |

## Security audits

| Date | Type | Report |
|---|---|---|
| 2026-07-22 | Pre-public | [docs/security-audits/2026-07-22-pre-public.md](security-audits/2026-07-22-pre-public.md) |
| 2026-07-24 | Post-hardening | Pending — see [issue #69](https://github.com/codegiveness/mssql-mcp/issues/69) |

## CODEOWNERS

[.github/CODEOWNERS](../.github/CODEOWNERS) assigns `@codegiveness/mssql-mcp-maintainers` to every path, with extra precision for the Guard, ADRs, workflows, and npm distribution. It is enforced through branch protection (`require_code_owner_reviews: true`).

## OpenSSF Best Practices

Self-assessment at [bestpractices.dev](https://bestpractices.dev/) is pending. This is a manual human task tracked in [issue #68](https://github.com/codegiveness/mssql-mcp/issues/68).
