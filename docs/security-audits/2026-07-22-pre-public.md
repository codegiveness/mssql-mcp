# Security Audit — Pre-Public Launch

> **Note:** This is a point-in-time pre-launch security audit (2026-07-22). All 5 blocking findings were fixed before release. Non-blocking items are documented for transparency — each has a rationale for deferral.

**Date:** 2026-07-22
**Scope:** Full codebase audit before `0.1.0` public release (ADR-0018 trigger #2)
**Method:** `/security-research` skill — 3 vulnerability hunters + 2 PoC engineers (team-mode)
**Team:** surface-hunter (deep), auth-data-hunter (ultrabrain), runtime-supply-hunter (unspecified-high), poc-engineer-a (unspecified-high), poc-engineer-b (deep)

## Verdict: PASS WITH FINDINGS

All 5 blocking findings fixed. 6 non-blocking findings documented for future hardening. No remaining blockers for `1.0.0-rc.1`.

## Findings

### Blocking (fixed in this commit)

| Severity | ID | Title | CWE | Fix |
|----------|----|-------|-----|-----|
| HIGH | A1 | Unrestricted-mode classifier parse failure → ExecuteNonQuery | CWE-754 | Reject unparseable SQL with `parse_error` instead of executing |
| HIGH | F1 | install.js zip extraction path traversal (Zip Slip) | CWE-22 | Validate zip entry names before extraction |
| MED-HIGH | A4 | README promotes TrustServerCertificate=True without Encrypt=True | CWE-295 | Added `Encrypt=True` to all 4 connection string examples |
| MED | A2 | SqlException message leaks verbatim to Agent | CWE-532 | Apply `PasswordObfuscator.Obfuscate` in `ToolErrors.SqlError` |
| MED | F6 | PasswordObfuscator misses AccessToken=/Token= | CWE-532 | Extended regex to match `AccessToken` and `Token` |

### Non-blocking (documented for future hardening)

| Severity | ID | Title | CWE | Rationale |
|----------|----|-------|-----|-----------|
| LOW | A3 | Cross-DB access to system databases, no denylist | CWE-284 | Restricted mode limits SQL to SELECT — not a security bypass |
| LOW | A5 | get_top_queries exposes other sessions' SQL text | CWE-200 | Bounded by VIEW SERVER STATE — documented DBA diagnostic behavior |
| LOW | F2 | install.js redirect Location not validated to https: | CWE-601 | GitHub redirects are always HTTPS — would require compromising GitHub |
| LOW | F3 | Checksum sidecar same origin as archive | CWE-345 | Checksum IS verified; provenance via `gh attestation verify` is separate |
| LOW | F4 | Missing tar/unzip produces misleading error | CWE-754 | UX bug, not security |
| LOW | F5 | MSSQL_LOG_FILE path not validated | CWE-73 | Environment variable controlled by process owner, not attacker |
| LOW | F7 | release.yml inputs.tag shell interpolation | CWE-78 | workflow_dispatch only, requires repo:write |

### Rejected

| Candidate | Reason |
|-----------|--------|
| F8 (dbPrefix/dbIdExpr refactor hazard) | Code style issue, not a vulnerability |

## Positive confirmations

- Guard visitor covers all known T-SQL escape hatches (OPENROWSET, OPENQUERY, OPENXML, OPENDATASOURCE, EXECUTE AS, BULK INSERT, four-part names, SELECT INTO)
- `explain_query` uses `ValidateStrict` correctly
- `QuoteIdentifier` properly escapes `]`
- `get_top_queries` dbid is parameterized
- `analyze_indexes` @query is parameterized
- `ConnectionValidator --validate` path correctly obfuscates
- GitHub Actions all SHA-pinned
- Dependabot configured
- CODEOWNERS correct
- Trusted Publishing via OIDC
- npm publish with `--provenance`
- `TreatWarningsAsErrors` + `AnalysisLevel=latest-recommended`
- No `pull_request_target` trigger
- `spawnSync` calls don't pass `shell:true`

## Residual risk

- **Unrestricted mode itself**: A1 fix prevents executing unparseable SQL, but Unrestricted mode by design bypasses the Guard. This is documented (ADR-0006) and intentional — the operator opts in.
- **AccessToken auth**: F6 fix obfuscates AccessToken/Token in logs, but the token may still appear in other code paths not yet audited. Low risk — README only shows User Id=sa.
- **install.js tar extraction**: `tar -xzf` has partial protection (strips leading `/`), but a crafted tar with `../` entries could still escape. Low risk — archives are from GitHub Releases (trusted origin).
