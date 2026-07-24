# Security Policy

## Reporting a Vulnerability

Please report security vulnerabilities using [GitHub's private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability). Click **"Report a vulnerability"** on the **Security** tab of this repository.

**Do not open a public issue for security vulnerabilities.**

## Scope

These issues are in scope:

- **Guard bypass**: SQL that executes in Restricted mode when it should be rejected (e.g. DML/DDL slipping past the AST allowlist, nested statement evasion, `QuoteIdentifier` bypass enabling cross-DB access).
- **Credential leakage**: connection strings or passwords appearing in stdout (MCP JSON-RPC channel), logs at non-debug levels, or error messages returned to the Agent.
- **`bin/mssql-mcp.js` tampering**: checksum bypass, binary substitution, or archive extraction path traversal in the shim's self-heal download path.
- **Authentication bypass**: connection string manipulation that bypasses intended auth restrictions.

### Restricted mode scope clarification

Restricted mode is **statement-type safety**, not **data-scope safety**. The Guard allows `SELECT` against any database the connection's SQL login can access — including 3-part cross-database names like `OtherDb.dbo.Users`. The `BEGIN TRAN ... ROLLBACK` wrapper prevents writes but does not prevent reads; the resultset is returned to the Agent before the rollback fires. To enforce tenant isolation, grant the SQL login least-privilege permissions scoped to a single database. See [docs/security-audits/2026-07-24-post-hardening.md](docs/security-audits/2026-07-24-post-hardening.md) finding AHD-1.

These issues are **out of scope** — report them upstream:

- SQL Server product vulnerabilities → [Microsoft Security Response Center (MSRC)](https://msrc.microsoft.com/)
- MCP protocol spec vulnerabilities → [MCP spec repository](https://github.com/modelcontextprotocol/modelcontextprotocol)
- Dependency CVEs → open a normal issue or PR with the bumped version

## Supply Chain

NuGet packages are repository-signed by NuGet.org and published via Trusted Publishing (OIDC from GitHub Actions — no long-lived API keys). GitHub Release artifacts (tarballs, zips, checksums) and npm packages are provenance-attested via `actions/attest@v4`. NuGet packages are NOT provenance-attested because NuGet.org re-signs packages on ingestion, breaking the SHA (see [NuGetGallery#10026](https://github.com/NuGet/NuGetGallery/issues/10026)); tracking native NuGet provenance via [NuGet/Home#13581](https://github.com/NuGet/Home/issues/13581). All GitHub Actions are pinned to commit SHAs. Dependabot keeps github-actions and nuget dependencies current.

## Response SLA

| Step | Target |
|---|---|
| Acknowledge receipt | 48 hours |
| Initial assessment (valid/duplicate/out-of-scope) | 14 days |
| Fix or coordinated public disclosure | 90 days from initial assessment |

These SLAs reflect solo-maintained project capacity. If you need faster disclosure for active exploitation, say so in the report and we'll prioritize.

## Safe Harbor

Good-faith security research on this project is welcomed. We will not pursue legal action against researchers who:

- Make a good-faith effort to avoid privacy violations, destruction of data, and interruption of service.
- Report vulnerabilities privately per the process above, giving reasonable time to remediate before any public disclosure.
- Do not test against production databases they do not own or have explicit permission to test.

## What NOT to Do

- Do not test vulnerabilities against production databases you do not own.
- Do not open a public GitHub issue describing the vulnerability.
- Do not exploit the vulnerability beyond the minimum needed to demonstrate it.
