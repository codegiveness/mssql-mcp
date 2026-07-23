# Definition of "100% worked" for the onboarding surface

The onboarding surface is considered "working" when install succeeds (A), the server starts and completes the MCP handshake (B), and `mssql-mcp --validate` confirms the connection string reaches SQL Server (C) — observable as a single command exiting 0, with no extra reading required. First-query-returns (D) is explicitly out of scope: that's the agent's job, not ours, but `--validate` proves the pipe is live before the agent ever calls a tool.

## Context

The goal was stated as "easy to use, install and always 100% worked." That phrase is fuzzy and unfalsifiable as written. Surveying three reference repos (codegraph, rtk, agentmemory) showed each ships an explicit post-install verify command (`codegraph status`, `rtk gain`, `iii --version` / `curl /health`). mssql-mcp had `--validate` implemented in `Program.cs` (per SPEC user story #15) but undocumented in the README — so the proof command existed but was invisible to users.

## Decision

**"100% worked" = A + B + C**, where:
- **A — Install succeeds**: the documented one-liner downloads a working binary; `npx -y mssql-mcp-cli` (or `dotnet tool install -g codegiveness.mssql-mcp`) exits 0 and places the binary.
- **B — Server starts**: the process launches and completes the MCP `initialize` handshake; the agent receives the tool list.
- **C — Connection validates**: `mssql-mcp --validate` opens a connection to SQL Server, runs `SELECT 1`, closes, exits 0. On failure, the error names which layer broke (binary missing, config malformed, connection refused, auth failed).

The user-visible proof is `--validate` exiting 0. This makes "worked" falsifiable: one command, exit 0 or not, labeled failure mode.

**D — First query returns** (an agent calling `list_databases` and getting a JSON array) is explicitly **out of scope** for our definition of "worked." It depends on the agent's prompt, the harness wiring, and a live database with granted permissions — none of which the server controls. `--validate` proves the pipe is live; whether the agent then uses it correctly is the agent's job.

## Considered Options

- **A only ("install succeeds")** — rejected. A binary that installs but can't start or can't connect is not "worked." Reference repos all verify beyond install.
- **A + B ("server starts")** — rejected. A server that starts but can't reach the DB leaves the user debugging a cryptic "connection failed" error on first tool call. The proof must include the connection.
- **A + B + C ✅** — chosen. `--validate` already exists, covers install+start+connection in one command, and produces a labeled failure if any layer breaks.
- **A + B + C + D ("first query returns")** — rejected. D depends on the agent and harness, not the server. We can't make a falsifiable server-side guarantee about D without also guaranteeing the agent's behavior.

## Consequences

- `--validate` becomes the documented proof command and must be surface in every install path's docs.
- Every install path (npm, dotnet tool) must be tested against A+B+C in CI, not just A.
- README and any harness-specific config snippets must point to `--validate` as the "did it work?" step.
- Failures in `--validate` must produce labeled errors (which layer broke), not stack traces — this is a docs and error-shape contract, not just a code one.
- D (first query) remains a user story, not a "worked" criterion.
