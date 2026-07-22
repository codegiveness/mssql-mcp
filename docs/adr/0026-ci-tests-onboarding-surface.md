# CI tests the onboarding surface, not just the code

Add a CI job that runs the documented install one-liner and verifies it produces a runnable binary (`--version`) and a working connection path (`--validate` against Azure SQL Edge). Add a README snippet lint step that parses every JSON config block in the README and confirms it's valid JSON with the expected `mcpServers` shape. Harness wiring (layer B) stays manual per ADR-0022.

## Context

Current CI runs `dotnet build` + `dotnet test` — code only. Nothing verifies the onboarding surface: the documented `npx` one-liner, the `--validate` path, or the validity of README config snippets. A typo like `npx -y mssql-mcp` (wrong package — ADR-0023) shipped in the README undetected. Reference repos all test the install + verify command in CI: codegraph runs install + `codegraph init` + `status`; rtk runs install + `rtk --version`; agentmemory runs install + `iii --version` + `curl /health`.

## Decision

### Smoke job (new CI job, runs on push to main + on release)

1. `npx -y @codegiveness/mssql-mcp --version` — proves layer A (install produces a runnable binary). Catches broken package names, broken install.js, missing binary in archive.
2. `npx -y @codegiveness/mssql-mcp --validate` against an Azure SQL Edge container — proves layer C (connection path end-to-end). Reuses the existing Azure SQL Edge container pattern from integration tests.

Does not test layer B (harness wiring) — that stays manual per ADR-0022.

### README snippet lint (new step in CI)

A ~20-line Node script that extracts every fenced JSON block from `README.md`, parses each as JSON, and asserts:
- It's valid JSON (catches malformed snippets).
- It contains an `mcpServers` key (catches structurally wrong config).
- Each server entry has `command` and either `args` or `env` (catches the `npx -y mssql-mcp` class of typo — wrong package name, missing keys).

Runs on every push/PR that touches `README.md` or `npm/package.json`.

## Considered Options

- **A. Current (code-only CI)** — rejected. The `npx -y mssql-mcp` typo shipped undetected. CI passes while the documented install is broken.
- **B. Smoke job only (no README lint)** — rejected. Catches install/binary failures but not doc typos. The typo we found was a README problem, not a code problem — a smoke job that runs the *correct* command wouldn't catch the README saying the *wrong* command.
- **C. Smoke job + README lint ✅** — chosen. Covers both the code path (install + validate) and the doc path (snippets are valid and structurally correct). The lint step is cheap and catches the exact failure class that motivated this ADR.

## Consequences

- CI gains one job (smoke test) and one step (README lint). Estimated +2-3 min to CI runtime; Azure SQL Edge container spin-up is the long pole but already proven in integration tests.
- Breaking the documented one-liner, breaking `--validate`, or shipping malformed README config snippets now fails CI before merge.
- The README lint script must be updated if the harness config structure changes (e.g. adding a new harness snippet) — low maintenance, but a new surface.
- Layer B (harness wiring) remains untested in CI by design — manual verification per ADR-0022 is the contract.
