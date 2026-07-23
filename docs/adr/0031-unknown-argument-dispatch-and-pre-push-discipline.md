# Unknown-argument dispatch and pre-push discipline

The binary treats any unrecognized CLI argument as a no-op: it falls through to `MssqlMcpOptions.Parse()`, which throws `[startup] Missing SQL Server connection string` when no connection string is set. A user who typed `mssql-mcp upgrade` (an intuitive thing to try) received a database error that had nothing to do with upgrading. This ADR fixes the unknown-argument UX and installs a verifiable pre-push discipline so the mistake is caught before it reaches users.

## Context

A maintainer ran `mssql-mcp upgrade` expecting the binary to self-update and instead saw:

```
[startup] Missing SQL Server connection string. Set MSSQL_CONNECTION_STRING env var or pass --connection-string.
```

Root cause (confirmed by reading `src/mssql-mcp/Program.cs` and `src/mssql-mcp.Core/Configuration/MssqlMcpOptions.cs`):

1. There is no `upgrade` command anywhere in the codebase (zero matches for "upgrade" in `*.cs`).
2. `Program.cs` checks exactly one command before calling `MssqlMcpOptions.Parse()` — `--version` (line 14). Every other argument flows into `Parse()`.
3. `Parse()` (line 78-82) throws `InvalidOperationException` with the connection-string message when `MSSQL_CONNECTION_STRING` is unset, regardless of what the user was actually trying to do.
4. The error message names the connection string because the binary assumed it was starting the MCP server, not upgrading.

This is the same class of defect that ADR-0029 addressed for `--validate` in the README hero: a command that requires configuration surfaced where no configuration exists. ADR-0029 fixed the *positioning*; this ADR fixes the *code path*.

## Decision

### 1. Add `--help` / `-h` as a real command (exit 0)

`Program.cs` gains a `--help` / `-h` check before `MssqlMcpOptions.Parse()`, alongside the existing `--version` check. It prints a usage block and exits 0. Every CLI tool is expected to have `--help`; its absence is itself a "dumb factor."

### 2. Unknown arguments produce a graceful error (exit 1)

Any argument that is not a recognized flag and not a value for a recognized flag produces:

```
mssql-mcp: unknown argument '<arg>'.

Usage: mssql-mcp [options]
  (no args)              Start the MCP stdio server (default)
  --version              Print version and exit
  --help, -h             Print this help and exit
  --validate             Test the SQL Server connection and exit
  --connection-string    SQL Server connection string (env: MSSQL_CONNECTION_STRING)
  --access-mode          restricted | unrestricted (default: restricted)
  --query-timeout        Per-query timeout in seconds (default: 30)
  --log-level            trace | debug | info | warning | error | critical (default: info)

To update mssql-mcp:
  npm install -g @codegiveness/mssql-mcp@latest
  # or
  dotnet tool update -g codegiveness.mssql-mcp
```

The "To update" section is included because `upgrade` is the single most common unknown command users type when they want a newer version, and pointing them at the correct update path is the actual answer to their intent.

### 3. No real `upgrade` command

The binary does not self-upgrade. Self-updating requires shelling out to `npm` or `dotnet`, detecting the install method, handling permissions, and platform differences — real complexity for a feature most CLI tools don't have. The graceful unknown-arg error (decisions 1+2) fixes `upgrade`, `udpate`, `--halp`, and every other typo with one code path. A real `upgrade` command can be revisited if the unknown-arg error proves insufficient.

### 4. Secrets stay in `.env` (gitignored); never in AGENTS.md or any committed file

The maintainer's real connection string lives in `.env`, which is gitignored (`.gitignore` lines: `.env`, `.env.local`). `AGENTS.md` is also gitignored (line 119), so even writing the secret there would not leak to GitHub — but the secret still enters AI session context when pasted and could leak if the file is shared, screenshotted, or un-ignored. The discipline is:

- `.env` holds the real `MSSQL_CONNECTION_STRING`. It already exists and is gitignored.
- `AGENTS.md` references `.env` by name and carries the pre-push checklist **without** the password.
- A committed `.env.example` template (with angle-bracket placeholders, per ADR-0029 §3) shows the expected shape.

### 5. Pre-push checklist in `CONTRIBUTING.md` (committed)

`CONTRIBUTING.md` gains a "Pre-push checklist" section so the discipline survives even if `AGENTS.md` is lost. The checklist is the verification bar (decision 6 below), stated as a repeatable procedure.

### 6. Verification bar: full matrix against a live DB

Before any push to GitHub, all of the following must pass:

| Check | Command | What it proves |
|---|---|---|
| Unit tests | `dotnet test --filter Category!=Integration` | Guard AST validation, options parsing, error shapes |
| Integration tests | `INTEGRATION=true MSSQL_CONNECTION_STRING=... dotnet test` | All 9 tools against a real SQL Server |
| Version | `mssql-mcp --version` | Binary resolves and runs |
| Validate | `MSSQL_CONNECTION_STRING=... mssql-mcp --validate` | Connection works (SELECT 1) |
| Help (new) | `mssql-mcp --help` | New command works, exits 0 |
| Unknown arg (new) | `mssql-mcp upgrade` | New graceful error, exits 1 |
| npm smoke test | `node npm/test.js` | Shim parses, RID mapping, checksum parser |
| LSP diagnostics | `lsp_diagnostics` on changed files | No type errors introduced |

If any check fails, the push is blocked. No exceptions.

## Considered Options

- **A. Pre-`Parse` arg dispatch + `--help` + unknown-arg error ✅** — chosen. Minimal change to `Program.cs`: add `--help`/`-h` and an unknown-arg detector before `Parse()`. Fixes every unknown command, not just `upgrade`. No restructuring of `Parse()` needed.

- **B. Restructure `Parse()` into two phases (parse + `RequireConnectionString()`) — rejected.** Cleaner separation, but touches `Parse()`, its 45 callers, and the test suite. Over-engineering for a UX fix. The current error message for `--validate` without a connection string is already correct — it names both the env var and the CLI flag.

- **C. Add a real `upgrade` command — rejected.** The binary cannot update its own npm package or dotnet tool from inside itself without shelling out and detecting the install method. The unknown-arg error (decision 2) answers the user's actual intent ("how do I update?") without the complexity.

- **D. Commit the connection string to `AGENTS.md` (gitignored) — rejected.** `AGENTS.md` is gitignored today, but gitignore rules can be removed, files can be copied, and secrets in instruction files enter AI session context on every paste. `.env` is the correct home for secrets; `AGENTS.md` carries the discipline, not the secret.

## Consequences

- **Every unrecognized argument now produces a useful error.** `mssql-mcp upgrade`, `mssql-mcp foo`, `mssql-mcp --halp` all print the usage block with update guidance and exit 1. No more connection-string errors for non-server operations.
- **`--help` and `-h` are first-class commands.** Discoverable via the standard convention. Exit 0 so they compose with `&&` and shell scripts.
- **`--validate` without a connection string is unchanged.** Its error message already names the env var and CLI flag. The user forgot to configure; the message tells them what to do. No special-casing needed.
- **Secrets never leave `.env`.** `AGENTS.md` and `CONTRIBUTING.md` carry the discipline without the secret. `.env.example` (committed) shows the shape with angle-bracket placeholders.
- **Pre-push discipline is verifiable.** The checklist in `CONTRIBUTING.md` is a table of commands with expected outcomes. An AI agent or human contributor can run it mechanically. If the integration tests can't reach a live DB, the push is blocked — no "it probably works" pushes.
- **`Program.cs` gains a small arg-dispatch block before `Parse()`.** The existing `--version` check (line 14) is joined by `--help`/`-h` and the unknown-arg detector. `Parse()` is unchanged. The blast radius is one file.
