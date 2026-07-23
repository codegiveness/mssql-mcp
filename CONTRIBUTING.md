# Contributing to mssql-mcp

Thanks for your interest in contributing. This document covers the development workflow, coding standards, and PR process.

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git
- (Optional) Docker — for integration tests against Azure SQL Edge

### Getting started

```bash
git clone https://github.com/codegiveness/mssql-mcp.git
cd mssql-mcp
dotnet restore
dotnet build
```

### Project layout

Three projects, one solution:

```
mssql-mcp.sln
src/
  mssql-mcp.Core/       # Guard, SqlExecutor, Options — no MCP deps
  mssql-mcp.Tools/      # 9 tool classes with [McpServerTool]
  mssql-mcp/            # App: Program.cs, DI, stdio, CLI, npm entrypoint
tests/
  mssql-mcp.Core.Tests/ # Guard AST validation, type coercion, etc.
  mssql-mcp.Tools.Tests/ # Tool attribute wiring, schema tests
npm/                    # npm package (shim + per-platform binaries)
docs/adr/               # Architectural Decision Records
```

The dependency graph is `Core ← Tools ← App`. Cross-project references enforce wiring at compile time — code that uses `Microsoft.Data.SqlClient` or ScriptDom lives in Core; code that uses the MCP SDK lives in Tools; code that wires DI and stdio lives in App.

## Running Tests

### Unit tests (fast, no external dependencies)

```bash
dotnet test --filter Category!=Integration
```

This is what CI runs. ~50-80 tests, completes in seconds.

### Integration tests (requires live SQL Server)

```bash
# Start Azure SQL Edge container
docker run -e "ACCEPT_EULA=1" -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" \
  -p 1433:1433 --name mssql-edge -d mcr.microsoft.com/azure-sql-edge:latest

# Run integration tests
INTEGRATION=true MSSQL_CONNECTION_STRING="Server=localhost;User Id=sa;Password=YourStrong!Passw0rd;Encrypt=True;TrustServerCertificate=True;" dotnet test
```

Integration tests are tagged `[Trait("Category", "Integration")]` and skipped by default.

## Pre-push checklist

Run these checks before pushing or opening a PR. If any check fails, the push is blocked — no exceptions.

| Check | Command | Expected outcome |
|-------|---------|------------------|
| Unit tests | `dotnet test --filter Category!=Integration` | All pass, 0 failed |
| Integration tests | `INTEGRATION=true MSSQL_CONNECTION_STRING="..." dotnet test` | All pass (requires live SQL Server) |
| Validate connection | `dotnet run --project src/mssql-mcp -- --validate` (with `.env` loaded) | `[startup] Connection validated successfully.` exit 0 |
| Help command | `mssql-mcp --help` (or `dotnet run --project src/mssql-mcp -- --help`) | Prints usage block, exit 0 |
| Unknown-arg error | `mssql-mcp upgrade` (or `dotnet run --project src/mssql-mcp -- upgrade`) | `mssql-mcp: unknown argument 'upgrade'.` to stderr, exit 1 |
| npm smoke test | `node npm/test.js` (if applicable) | All smoke tests pass |
| LSP diagnostics | Run LSP diagnostics on changed files | 0 errors |
| MCP stdio smoke test | `./scripts/mcp-smoke.sh` (with `.env` loaded) | `[PASS] tools/list: 9 tools found` + `[PASS] list_databases: returned N databases` + `ALL CHECKS PASSED` |

## Coding Standards

### C# conventions

- C# 13 / .NET 10 target
- `var` only when the type is obvious from the right-hand side; otherwise use explicit types
- No `!` null-forgiving operator except at trust boundaries (deserialization, CLI parsing)
- No suppressions of nullable warnings (`#pragma warning disable CS8602`, `!`, etc.)
- No mutable static state — the MCP server is long-lived; static mutation causes race conditions
- All public APIs have XML documentation (`/// <summary>`)
- File-scoped namespaces (`namespace Foo;`)
- 4-space indentation

### Type safety

- No `as any` equivalents in C#: no `dynamic`, no `object` where a generic exists, no `!` suppressions
- If the type system fights you, the design is probably wrong — discuss in an ADR

### Dependencies

- Adding a new dependency requires updating `THIRD-PARTY-NOTICES.md` with the license and source link
- Transitive dependencies that ship in self-contained builds must be MIT or Apache-2.0 (no copyleft, no "Distributable Code" license unless we have a redistribution plan like the SNI workaround in ADR-0002)

## ADR Workflow

Architectural decisions live in [`docs/adr/`](./docs/adr/). Non-trivial changes require an ADR.

- Read existing ADRs before proposing changes that conflict with them
- Propose the ADR in the PR description; discuss before implementation
- ADRs are numbered sequentially (`0001`, `0002`, ...) — check the highest existing number and increment
- ADR format: see [`docs/adr/0001-dual-access-mode.md`](./docs/adr/0001-dual-access-mode.md) for an example

An ADR is required when a decision is:
1. Hard to reverse (changing your mind later costs real time)
2. Surprising without context (a future reader will wonder "why did they do it this way?")
3. The result of a real trade-off (genuine alternatives existed and you picked one for specific reasons)

If all three are true, write an ADR. If not, a code comment is enough.

## Commit Conventions

Use [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: add list_stored_procedures tool
fix: guard now rejects BEGIN...END nested DROP statements
docs: add ADR-0017 for connection pooling
test: add unit tests for QuoteIdentifier
refactor: extract SqlExecutor from Guard
chore: bump Microsoft.Data.SqlClient to 6.0.1
ci: add windows-arm64 to release matrix
```

This enables auto-changelog generation and matches the Semantic Versioning tags (`v0.1.0`, `v0.2.0`, etc.).

### Branch naming

No strict convention — just use descriptive names:
- `feat/guard-visitor`
- `fix/install-checksum`
- `docs/readme-quickstart`

## Pull Request Process

### PR checklist

- [ ] Tests pass (`dotnet test --filter Category!=Integration`)
- [ ] New code has unit tests
- [ ] No type suppressions or null-forgiving operators added
- [ ] Public APIs documented with XML comments
- [ ] ADR added/updated if an architectural decision changed
- [ ] `THIRD-PARTY-NOTICES.md` updated if a new dependency was added
- [ ] No secrets in code or tests

### Merge strategy

- **Squash-merge** all PRs — clean main history, one commit per PR, conventional-commit message becomes the squashed commit message
- All PRs require review before merge (even from the maintainer — use a second account or a trusted contributor)
- Force-push to `main` is disabled

### No CLA / DCO required

Contributions fall under the MIT license implicitly. If a corporate contributor ever needs a DCO for compliance, we'll add one then.

## Reporting Issues

- Bugs → GitHub Issues with reproduction steps
- Security vulnerabilities → see [SECURITY.md](./SECURITY.md) (do NOT open a public issue)
- Feature requests → GitHub Issues with use case description
