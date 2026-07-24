# AGENTS.md

Guidance for AI agents (and humans) working in this repo.

## Agent skills

### Issue tracker

GitHub Issues at `github.com/codegiveness/mssql-mcp` (via `gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical roles: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `CONTEXT.md` + `docs/adr/` at repo root. See `docs/agents/domain.md`.

## Local environment

- Load `.env` before running tests or `--validate`.
- `.env` is gitignored and stays local only. Use `.env.example` as the template. Never commit secrets.
- Secrets stay in `.env` only and don't enter AI session context unnecessarily.

## Pre-push checklist

If any check fails, the push is blocked — no exceptions.

| Check | Command | Expected outcome |
|-------|---------|------------------|
| Unit tests | `dotnet test --filter Category!=Integration` | All pass, 0 failed |
| Integration tests | `INTEGRATION=true MSSQL_CONNECTION_STRING="..." dotnet test` | All pass (requires live SQL Server) |
| Validate connection | `dotnet run --project src/mssql-mcp -- --validate` (with `.env` loaded) | `[startup] Connection validated successfully.` exit 0 |
| Help command | `mssql-mcp --help` (or `dotnet run --project src/mssql-mcp -- --help`) | Prints usage block, exit 0 |
| Unknown-arg error | `mssql-mcp upgrade` (or `dotnet run --project src/mssql-mcp -- upgrade`) | `mssql-mcp: unknown argument 'upgrade'.` to stderr, exit 1 |
| npm smoke test | `node npm/test.js` (if applicable) | All smoke tests pass |
| README linter | `node scripts/lint-readme-snippets.js` | All snippets valid + badge URLs well-formed |
| LSP diagnostics | Run LSP diagnostics on changed files | 0 errors |
| Format check | `dotnet format mssql-mcp.sln --verify-no-changes --no-restore` | Clean (no changes needed) |
| MCP stdio smoke test | `./scripts/mcp-smoke.sh` (with `.env` loaded) | `[PASS] tools/list: 9 tools found` + `[PASS] list_databases: returned N databases` + `ALL CHECKS PASSED` |

## Security verification (periodic)

These controls run automatically in CI or GitHub repo settings — not per-push. Verify them when reviewing the security posture:

| Control | Where | Expected |
|---------|-------|----------|
| OpenSSF Scorecard | `.github/workflows/scorecard.yml` (push + weekly cron) | Score published to Security tab, badge renders in README |
| SBOM (CycloneDX) | CI build step + GitHub Release attestation | `artifacts/sbom/*.bom.json` generated, attested via `actions/attest@v4` |
| Branch protection | GitHub repo settings (verified via `gh api repos/codegiveness/mssql-mcp/branches/main/protection`) | Enforce admins, code owners, dismiss stale reviews, 0 required reviews (solo — see ADR-0033) |
| Secret scanning | GitHub repo settings (verified via `gh api repos/codegiveness/mssql-mcp --jq '.security_and_analysis'`) | Secret scanning + push protection enabled |
| Dockerfile build | `.github/workflows/ci.yml` `docker` job | `docker build` + `docker run --rm mssql-mcp-test --version` passes |
| Security audits | `docs/security-audits/` | Pre-public (2026-07-22) + post-hardening (2026-07-24) reports present |

See [docs/security-posture.md](docs/security-posture.md) for the consolidated evidence page.

## MCP stdio smoke test — MANDATORY

This test is **not optional**. It must pass before every push, alongside unit tests and integration tests. It proves the server actually works as an MCP server over the stdio JSON-RPC transport — unit tests alone do not verify this.

The test uses the official MCP Inspector CLI (`@modelcontextprotocol/inspector --cli`) to perform a real handshake: `initialize` → `tools/list` → `tools/call list_databases`. The inspector spawns the server, sends JSON-RPC messages to its stdin, and reads responses from stdout — exactly what a real MCP client (Claude Desktop, Cursor) does.

Run it with:

```bash
# .env is auto-loaded by the script; or export manually:
export MSSQL_CONNECTION_STRING="Server=...;Database=...;User Id=...;Password=...;Encrypt=True;TrustServerCertificate=True;"
./scripts/mcp-smoke.sh
```

Expected output:

```
=== [1] initialize + tools/list ===
[PASS] tools/list: 9 tools found
=== [2] tools/call list_databases ===
[PASS] list_databases: returned N databases

================================
  PASSED: 2  FAILED: 0
================================
ALL CHECKS PASSED
```

If any check fails, the push is blocked — no exceptions.
