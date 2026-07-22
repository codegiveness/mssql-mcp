# `--validate` proves the connection; Troubleshooting section covers the harness wiring

`--validate` is the documented proof command for layer C (connection reaches SQL Server). It does not — and cannot — prove layer B (the harness invokes the server correctly and completes the MCP handshake). A new Troubleshooting section in the README covers layer B with per-harness pointers: where each client logs, what "connected" looks like, and the common wiring failure modes.

## Context

ADR-0021 defined "100% worked" as A (install) + B (server starts, handshake) + C (connection). `--validate` exists and proves C, but it runs from a terminal — it can't detect that the harness config JSON has the wrong `command` path, a missing env var, or a misconfigured stdio transport. The most common "it doesn't work" report will be "the agent doesn't see the server," which is a B-layer failure invisible to `--validate`.

Surveyed reference repos: none prove the harness→server wiring end-to-end from a single command. codegraph `status`, rtk `--version`, agentmemory `/health` all prove pieces and document the gaps.

## Decision

- **`--validate` is the proof command for C.** Documented as "run this after install to confirm your connection string works." Exits 0 with a success message, or non-zero with a labeled error (connection refused, auth failed, timeout, etc.).
- **`--validate` explicitly does not prove B.** This is documented, not hidden — the verify section says `--validate` checks the connection, and points to the Troubleshooting section for harness-wiring checks.
- **New Troubleshooting section** covers layer B per harness: where Claude Desktop / Cursor / VS Code / Windsurf / Cline / Continue each log MCP connections, what a successful connection looks like in each, and the top failure modes (wrong command path, missing `MSSQL_CONNECTION_STRING` in env block, npx not on PATH from the harness's spawn context).
- **No `--version` as a second pre-flight command.** `--version` proves the binary runs but not that the harness calls it — marginal value, and two commands is worse UX than one command plus a docs section.

## Considered Options

- **A. `--validate` only** — rejected. Leaves the biggest onboarding gap (harness wiring) unaddressed. "It doesn't work" reports will mostly be B-layer failures `--validate` can't catch.
- **B. `--validate` + Troubleshooting section ✅** — chosen. Honest about what `--validate` proves and what it doesn't. Matches the reference-repo pattern: prove what you can, document the gaps.
- **C. `--validate` + `--version` as a two-command pre-flight** — rejected. `--version` proves the binary runs but not that the harness invokes it correctly. Marginal value over `--validate` alone, at the cost of two commands instead of one.

## Consequences

- `--validate`'s error output must be labeled by failure layer (connection refused, auth failed, timeout, binary-not-found) so users can self-diagnose without reading code.
- README gains a Troubleshooting section with per-harness subsections for the 6 documented harnesses (ADR-0022).
- The verify step in every install path's docs says: run `--validate`; if it passes but the agent still can't see the server, see Troubleshooting.
- `--version` stays as a CLI flag but is not positioned as a verification step.
