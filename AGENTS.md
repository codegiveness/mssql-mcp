# AGENTS.md

Guidance for AI coding agents (Codex, Cursor, Copilot, Claude Code, omp, and others) and humans working in this repository. Claude Code reads it through `CLAUDE.md`.

## What this is

`mssql-mcp` is a Model Context Protocol server for Microsoft SQL Server, written in C# on .NET 11. It exposes 9 tools over stdio JSON-RPC. It ships as a NuGet `dotnet tool`, an npm wrapper with per-platform self-contained binaries, a Docker image, and GitHub Release archives.

Read before changing code:

- [`CONTEXT.md`](CONTEXT.md): domain glossary. Use its terms in code, issues, and PR text.
- [`docs/adr/`](docs/adr/): architectural decisions. If your change contradicts an ADR, say so explicitly and add or amend an ADR.
- [`CONTRIBUTING.md`](CONTRIBUTING.md): project layout (`Core ← Tools ← App`), coding standards, dependency licensing rules, commit and PR conventions.
- [`SECURITY.md`](SECURITY.md): do not open public issues for vulnerabilities.

## Ground rules

- Keep the diff to the task. No drive-by refactors, renames, or reformatting.
- `TreatWarningsAsErrors` is on. Fix warnings; do not add suppressions without a justification comment in the existing style.
- Packages are managed centrally in `Directory.Packages.props` with lock files (`packages.lock.json` plus per-RID `packages.<rid>.lock.json`). After a package change, regenerate the affected lock files with the same restore flags the Release workflow uses, and commit them.
- New shipped dependencies must be MIT or Apache-2.0 and be listed in `THIRD-PARTY-NOTICES.md`. Native SNI assets stay excluded (ADR-0002).
- Use Conventional Commits; PR titles are checked by `semantic.yml`. Versions are set by release-please. Never edit version stamps by hand and never cut a 1.x release.

## Local environment

- Copy `.env.example` to `.env` and set `MSSQL_CONNECTION_STRING` (use a user database, not `master`). Load it before integration tests, `--validate`, or the MCP smoke test.
- `.env` is gitignored. Never commit secrets or print connection strings into logs, issues, or chat.
- Node 24 is needed for the locked MCP Inspector and npm tooling under `.config/`.

## Issue tracker and triage

GitHub Issues at `github.com/codegiveness/mssql-mcp`, used through the `gh` CLI. See [`docs/agents/issue-tracker.md`](docs/agents/issue-tracker.md) and the five triage labels in [`docs/agents/triage-labels.md`](docs/agents/triage-labels.md). Domain-doc conventions: [`docs/agents/domain.md`](docs/agents/domain.md).

## Pre-push checklist

If any check fails, the push is blocked. No exceptions.

|Check|Command|Expected outcome|
|---|---|---|
|Unit tests|`dotnet test --solution mssql-mcp.sln -- --filter-not-trait Category=Integration`|All pass, 0 failed|
|Integration tests|`INTEGRATION=true MSSQL_CONNECTION_STRING="..." dotnet test --solution mssql-mcp.sln`|All pass (requires live SQL Server)|
|Validate connection|`dotnet run --project src/mssql-mcp -- --validate` (with `.env` loaded)|`[startup] Connection validated successfully.` exit 0|
|Help command|`mssql-mcp --help` (or `dotnet run --project src/mssql-mcp -- --help`)|Prints usage block, exit 0|
|Unknown-arg error|`mssql-mcp upgrade` (or `dotnet run --project src/mssql-mcp -- upgrade`)|`mssql-mcp: unknown argument 'upgrade'.` to stderr, exit 1|
|npm smoke test|`node npm/test.js` (if applicable)|All smoke tests pass|
|README linter|`dotnet run --project tools/MssqlMcp.RepoTool -- lint-readme-snippets`|All snippets valid + badge URLs well-formed|
|Version consistency|`dotnet run --project tools/MssqlMcp.RepoTool -- check-version-consistency`|`Version consistency: all stamps match.` exit 0|
|LSP diagnostics|Run LSP diagnostics on changed files|0 errors|
|Format check|`dotnet format mssql-mcp.sln --verify-no-changes --no-restore`|Clean (no changes needed)|
|MCP stdio smoke test|`dotnet run --project tools/MssqlMcp.RepoTool -- mcp-smoke` (with `.env` loaded)|`[PASS] tools/list: 9 tools found` + `[PASS] list_databases: returned N databases` + `ALL CHECKS PASSED`|

## MCP stdio smoke test — MANDATORY

Unit tests do not prove the server works as an MCP server. The smoke test uses the official MCP Inspector CLI (`@modelcontextprotocol/inspector --cli`) to perform a real handshake over stdio: `initialize` → `tools/list` → `tools/call list_databases`, exactly as Claude Desktop or Cursor would.

```bash
# .env is auto-loaded by the C# tool; or export manually:
export MSSQL_CONNECTION_STRING="Server=...;Database=...;User Id=...;Password=...;Encrypt=True;TrustServerCertificate=True;"
dotnet run --project tools/MssqlMcp.RepoTool -- mcp-smoke
```

Expected output:

```
=== [1] initialize + tools/list ===
[PASS] tools/list: 9 tools found
=== [2] tools/call list_databases ===
[PASS] list_databases: returned N databases
=== [3] idempotentHint annotations ===
[PASS] idempotentHint: 8 read-only=true, execute_sql=false

================================
  PASSED: 3  FAILED: 0
================================
ALL CHECKS PASSED
```

## Security posture

Scorecard, SBOM, branch protection, secret scanning, Docker checks, and audit reports are summarized in [`docs/security-posture.md`](docs/security-posture.md).
